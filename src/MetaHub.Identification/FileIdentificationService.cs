using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Identification.AniDb;
using MetaHub.Identification.Hashing;
using MetaHub.Infrastructure;

namespace MetaHub.Identification;

/// <summary>
/// The Shoko-style core (M3): identify a concrete local file exactly, rather than guessing
/// from its name. For anime this means ED2K hashing → AniDB file lookup → link the file to
/// the matching <see cref="Work"/> (and, where possible, <see cref="Episode"/>).
/// </summary>
public class FileIdentificationService
{
    private const double AniDbConfidence = 0.99;

    private readonly MetaHubDbContext _db;
    private readonly IAniDbClient _aniDb;
    private readonly ILogger<FileIdentificationService> _log;

    public FileIdentificationService(
        MetaHubDbContext db, IAniDbClient aniDb, ILogger<FileIdentificationService> log)
    {
        _db = db;
        _aniDb = aniDb;
        _log = log;
    }

    /// <summary>
    /// Identifies the file at <paramref name="path"/> via ED2K + AniDB. The file is recorded
    /// in <see cref="MetaHubDbContext.MediaFiles"/> regardless of whether a match is found, so
    /// its hash is cached and never recomputed.
    /// </summary>
    public async Task<IdentificationResult> IdentifyAnimeFileAsync(
        string path, bool forceRehash = false, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        var file = await _db.MediaFiles.FirstOrDefaultAsync(f => f.Path == fullPath, ct);

        // Fast path: already identified and not forced.
        if (file is { WorkId: not null } && !forceRehash)
        {
            return Result(file, identified: true, "Already identified (cached).");
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("File not found.", fullPath);

        file ??= new MediaFile { Path = fullPath };
        if (file.Id == Guid.Empty) file.Id = Guid.NewGuid();

        if (string.IsNullOrEmpty(file.Ed2kHash) || forceRehash)
        {
            file.SizeBytes = info.Length;
            file.Ed2kHash = await Ed2kHasher.ComputeFileAsync(fullPath, ct);
            _log.LogInformation("Hashed {Path}: ed2k={Ed2k} size={Size}", fullPath, file.Ed2kHash, file.SizeBytes);
        }

        if (_db.Entry(file).State == EntityState.Detached)
            _db.MediaFiles.Add(file);

        if (!_aniDb.IsEnabled)
        {
            await _db.SaveChangesAsync(ct);
            return Result(file, identified: false,
                "Hashed and recorded. AniDB lookup is disabled; enable it to resolve the work.");
        }

        var aniDbResult = await _aniDb.LookupFileAsync(file.SizeBytes, file.Ed2kHash!, ct);
        if (aniDbResult is null)
        {
            await _db.SaveChangesAsync(ct);
            return Result(file, identified: false, "No AniDB match for this file.");
        }

        await LinkToWorkAsync(file, aniDbResult, ct);
        await _db.SaveChangesAsync(ct);

        return Result(file, file.WorkId is not null, file.WorkId is not null
            ? "Identified via AniDB."
            : "AniDB matched the file, but no work with that AniDB id is ingested yet.");
    }

    private async Task LinkToWorkAsync(MediaFile file, AniDbFileResult result, CancellationToken ct)
    {
        var workId = await _db.ExternalIds
            .Where(x => x.Source == ExternalIdSource.AniDb && x.ExternalValue == result.AnimeId)
            .Select(x => (Guid?)x.WorkId)
            .FirstOrDefaultAsync(ct);

        if (workId is null)
        {
            _log.LogInformation("AniDB anime {Aid} not ingested; file recorded without a work link.", result.AnimeId);
            return;
        }

        file.WorkId = workId;
        file.IdentifiedBy = IdentificationMethod.Ed2k;
        file.IdentifiedAt = DateTimeOffset.UtcNow;
        file.Confidence = AniDbConfidence;

        // Best-effort episode link when episodes are already present and the number is numeric.
        if (int.TryParse(result.EpisodeNumber, out var epNo))
        {
            file.EpisodeId = await _db.Episodes
                .Where(e => e.SeriesWorkId == workId && e.EpisodeNumber == epNo)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);
        }

        // Keep the raw AniDB response for later re-processing (enrichment can mine it).
        _db.RawPayloads.Add(new RawPayload
        {
            WorkId = workId.Value,
            Source = ExternalIdSource.AniDb,
            HttpStatus = 220,
            Body = System.Text.Json.JsonSerializer.Serialize(result)
        });
    }

    private IdentificationResult Result(MediaFile file, bool identified, string note) => new()
    {
        MediaFileId = file.Id,
        Identified = identified,
        WorkId = file.WorkId,
        EpisodeId = file.EpisodeId,
        Method = file.IdentifiedBy,
        Confidence = file.Confidence,
        Ed2kHash = file.Ed2kHash,
        Note = note
    };
}
