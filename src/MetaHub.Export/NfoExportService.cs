using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MetaHub.Infrastructure;

namespace MetaHub.Export;

/// <summary>
/// Produces NFO files for works, either as a string (for the API / testing) or written to a
/// directory next to the media (for Jellyfin's NFO reader).
/// </summary>
public class NfoExportService
{
    private readonly MetaHubDbContext _db;
    private readonly ILogger<NfoExportService> _log;

    public NfoExportService(MetaHubDbContext db, ILogger<NfoExportService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Builds the NFO XML for a work, or null if the work does not exist.</summary>
    public async Task<NfoDocument?> BuildAsync(Guid workId, CancellationToken ct = default)
    {
        var work = await _db.Works
            .Include(w => w.ExternalIds)
            .Include(w => w.Images)
            .Include(w => w.WorkGenres).ThenInclude(wg => wg.Genre)
            .Include(w => w.SeriesDetail)
            .Include(w => w.MusicDetail)
            .Include(w => w.BookDetail)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == workId, ct);

        if (work is null)
            return null;

        return new NfoDocument(NfoBuilder.FileNameFor(work), NfoBuilder.Build(work));
    }

    /// <summary>Builds the NFO and writes it into <paramref name="directory"/>. Returns the path.</summary>
    public async Task<string?> WriteAsync(Guid workId, string directory, CancellationToken ct = default)
    {
        var doc = await BuildAsync(workId, ct);
        if (doc is null)
            return null;

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, doc.FileName);
        await File.WriteAllTextAsync(path, doc.Content, ct);
        _log.LogInformation("Wrote NFO {Path}", path);
        return path;
    }
}

public record NfoDocument(string FileName, string Content);
