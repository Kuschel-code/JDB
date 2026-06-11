using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// M2 ingest: turns the manami + Fribb mapping datasets into <see cref="Work"/> master
/// records with their cross-provider <see cref="ExternalId"/>s. No external metadata is
/// fetched here — enrichment (M4) later resolves "id → fields".
/// </summary>
public class AnimeIngestService
{
    private readonly MetaHubDbContext _db;
    private readonly ILogger<AnimeIngestService> _log;

    public AnimeIngestService(MetaHubDbContext db, ILogger<AnimeIngestService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Upserts every manami entry. Works are de-duplicated by their external ids so a
    /// re-run updates in place instead of creating duplicates.
    /// </summary>
    public async Task<IngestResult> IngestManamiAsync(ManamiDataset dataset, CancellationToken ct = default)
    {
        var result = new IngestResult();

        // (source, value) -> workId for everything already known, so re-runs are idempotent.
        var index = await _db.ExternalIds
            .Select(x => new { x.Source, x.ExternalValue, x.WorkId })
            .ToDictionaryAsync(x => Key(x.Source, x.ExternalValue), x => x.WorkId, ct);

        // Works created during this run, so multiple entries sharing an id reuse one work.
        var created = new Dictionary<Guid, Work>();

        // Works that already have artwork — manami ships a poster/thumbnail URL per anime,
        // which we store for works without any image so every entry has art right after
        // ingest (before enrichment ever runs).
        var worksWithImages = (await _db.Images.Select(i => i.WorkId).Distinct().ToListAsync(ct))
            .ToHashSet();

        foreach (var entry in dataset.Data)
        {
            ct.ThrowIfCancellationRequested();

            var ids = ParseSources(entry.Sources);
            if (ids.Count == 0)
            {
                result.Skipped++;
                continue;
            }

            // Find an existing work via any of this entry's ids.
            Guid? existingId = null;
            foreach (var (source, value) in ids)
            {
                if (index.TryGetValue(Key(source, value), out var wid))
                {
                    existingId = wid;
                    break;
                }
            }

            Work work;
            if (existingId is { } id && created.TryGetValue(id, out var inRun))
            {
                work = inRun;
                result.Updated++;
            }
            else if (existingId is { } id2)
            {
                work = await _db.Works
                    .Include(w => w.ExternalIds)
                    .Include(w => w.SeriesDetail)
                    .FirstAsync(w => w.Id == id2, ct);
                created[work.Id] = work;
                result.Updated++;
            }
            else
            {
                work = new Work { MediaType = MediaType.Anime };
                _db.Works.Add(work);
                created[work.Id] = work;
                result.Created++;
            }

            ApplyEntry(work, entry);

            if (!worksWithImages.Contains(work.Id) && !string.IsNullOrWhiteSpace(entry.Picture))
            {
                _db.Images.Add(new Image
                {
                    WorkId = work.Id,
                    Type = ImageType.Poster,
                    Url = entry.Picture!,
                    Source = "manami",
                    Score = 50
                });
                if (!string.IsNullOrWhiteSpace(entry.Thumbnail) && entry.Thumbnail != entry.Picture)
                {
                    _db.Images.Add(new Image
                    {
                        WorkId = work.Id,
                        Type = ImageType.Thumb,
                        Url = entry.Thumbnail!,
                        Source = "manami",
                        Score = 40
                    });
                }
                worksWithImages.Add(work.Id);
                result.ImagesAdded++;
            }

            // Add any ids this work does not have yet, keeping the index in sync.
            foreach (var (source, value) in ids)
            {
                var key = Key(source, value);
                if (index.ContainsKey(key))
                    continue;

                work.ExternalIds.Add(new ExternalId
                {
                    WorkId = work.Id,
                    Source = source,
                    ExternalValue = value
                });
                index[key] = work.Id;
                result.ExternalIdsAdded++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation(
            "manami ingest done: {Created} created, {Updated} updated, {Ids} ids, {Skipped} skipped",
            result.Created, result.Updated, result.ExternalIdsAdded, result.Skipped);
        return result;
    }

    /// <summary>
    /// Merges Fribb rows onto existing works (matched by AniDB id), adding the
    /// TVDB/TMDB/IMDb cross-references that manami does not carry.
    /// </summary>
    public async Task<IngestResult> MergeFribbAsync(IEnumerable<FribbEntry> entries, CancellationToken ct = default)
    {
        var result = new IngestResult();

        // anidb value -> workId
        var byAniDb = await _db.ExternalIds
            .Where(x => x.Source == ExternalIdSource.AniDb)
            .ToDictionaryAsync(x => x.ExternalValue, x => x.WorkId, ct);

        // Existing (source,value) set to avoid duplicate inserts.
        var existing = (await _db.ExternalIds
                .Where(x => x.Source == ExternalIdSource.Tvdb
                         || x.Source == ExternalIdSource.Tmdb
                         || x.Source == ExternalIdSource.Imdb
                         || x.Source == ExternalIdSource.Kitsu
                         || x.Source == ExternalIdSource.Mal
                         || x.Source == ExternalIdSource.AniList)
                .Select(x => new { x.Source, x.ExternalValue })
                .ToListAsync(ct))
            .Select(x => Key(x.Source, x.ExternalValue))
            .ToHashSet();

        foreach (var row in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(row.AniDbId) ||
                !byAniDb.TryGetValue(row.AniDbId!, out var workId))
            {
                result.Skipped++;
                continue;
            }

            void TryAdd(ExternalIdSource source, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var key = Key(source, value!);
                if (!existing.Add(key)) return; // already present
                _db.ExternalIds.Add(new ExternalId
                {
                    WorkId = workId,
                    Source = source,
                    ExternalValue = value!
                });
                result.ExternalIdsAdded++;
            }

            TryAdd(ExternalIdSource.Tvdb, row.TvdbId);
            TryAdd(ExternalIdSource.Tmdb, row.TmdbId);
            TryAdd(ExternalIdSource.Imdb, row.ImdbId);
            TryAdd(ExternalIdSource.Kitsu, row.KitsuId);
            TryAdd(ExternalIdSource.Mal, row.MalId);
            TryAdd(ExternalIdSource.AniList, row.AniListId);
            result.Updated++;
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Fribb merge done: {Updated} matched, {Ids} ids added, {Skipped} skipped",
            result.Updated, result.ExternalIdsAdded, result.Skipped);
        return result;
    }

    /// <summary>
    /// Merges kawaiioverflow/arm rows onto existing works (matched by MAL or AniList id),
    /// adding the Japanese database ids (Annict, Syoboi Calendar).
    /// </summary>
    public async Task<IngestResult> MergeArmAsync(IEnumerable<ArmEntry> entries, CancellationToken ct = default)
    {
        var result = new IngestResult();

        var byMal = await _db.ExternalIds
            .Where(x => x.Source == ExternalIdSource.Mal)
            .ToDictionaryAsync(x => x.ExternalValue, x => x.WorkId, ct);
        var byAniList = await _db.ExternalIds
            .Where(x => x.Source == ExternalIdSource.AniList)
            .ToDictionaryAsync(x => x.ExternalValue, x => x.WorkId, ct);

        var existing = (await _db.ExternalIds
                .Where(x => x.Source == ExternalIdSource.Annict || x.Source == ExternalIdSource.Syobocal)
                .Select(x => new { x.Source, x.ExternalValue })
                .ToListAsync(ct))
            .Select(x => Key(x.Source, x.ExternalValue))
            .ToHashSet();

        foreach (var row in entries)
        {
            ct.ThrowIfCancellationRequested();

            Guid workId;
            if (row.MalId is { } mal && byMal.TryGetValue(mal.ToString(), out var wid1))
                workId = wid1;
            else if (row.AniListId is { } al && byAniList.TryGetValue(al.ToString(), out var wid2))
                workId = wid2;
            else
            {
                result.Skipped++;
                continue;
            }

            void TryAdd(ExternalIdSource source, int? value)
            {
                if (value is null) return;
                var key = Key(source, value.Value.ToString());
                if (!existing.Add(key)) return;
                _db.ExternalIds.Add(new ExternalId
                {
                    WorkId = workId,
                    Source = source,
                    ExternalValue = value.Value.ToString()
                });
                result.ExternalIdsAdded++;
            }

            TryAdd(ExternalIdSource.Annict, row.AnnictId);
            TryAdd(ExternalIdSource.Syobocal, row.SyobocalTid);
            result.Updated++;
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("ARM merge done: {Updated} matched, {Ids} Japanese-db ids added, {Skipped} skipped",
            result.Updated, result.ExternalIdsAdded, result.Skipped);
        return result;
    }

    private static void ApplyEntry(Work work, ManamiEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Title))
            work.CanonicalTitle = entry.Title;

        work.ReleaseYear ??= entry.AnimeSeason?.Year;
        work.Status = MapStatus(entry.Status);
        work.UpdatedAt = DateTimeOffset.UtcNow;

        if (entry.Episodes > 0)
        {
            work.SeriesDetail ??= new SeriesDetail { WorkId = work.Id };
            work.SeriesDetail.EpisodeCount = entry.Episodes;
        }
    }

    private static WorkStatus MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "FINISHED" => WorkStatus.Finished,
        "ONGOING" or "CURRENTLY" => WorkStatus.Ongoing,
        "UPCOMING" => WorkStatus.Announced,
        _ => WorkStatus.Unknown
    };

    private static List<(ExternalIdSource Source, string Value)> ParseSources(IEnumerable<string> sources)
    {
        var list = new List<(ExternalIdSource, string)>();
        foreach (var url in sources)
        {
            if (ManamiSourceParser.TryParse(url, out var source, out var value))
                list.Add((source, value));
        }
        return list;
    }

    private static string Key(ExternalIdSource source, string value) => $"{source}:{value}";
}

/// <summary>Counts describing what an ingest run did.</summary>
public class IngestResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int ExternalIdsAdded { get; set; }
    public int ImagesAdded { get; set; }
    public int Skipped { get; set; }
}
