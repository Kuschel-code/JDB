using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;

namespace MetaHub.Enrichment;

/// <summary>
/// Batch driver for enrichment. Spaces out work to respect provider rate limits (the per-call
/// resilience is handled by Polly; the delay here paces the overall throughput).
/// </summary>
public class EnrichmentRunner
{
    private readonly MetaHubDbContext _db;
    private readonly EnrichmentService _service;
    private readonly ILogger<EnrichmentRunner> _log;

    public EnrichmentRunner(MetaHubDbContext db, EnrichmentService service, ILogger<EnrichmentRunner> log)
    {
        _db = db;
        _service = service;
        _log = log;
    }

    /// <summary>
    /// Enriches up to <paramref name="maxItems"/> anime works, pausing between each.
    /// When <paramref name="onlyMissing"/> is true, only works that currently have no overview
    /// are processed (i.e. "only fetch ones without info").
    /// </summary>
    public async Task<int> EnrichAnimeAsync(
        int maxItems = 100,
        int delayMs = 1000,
        bool forceRefresh = false,
        bool onlyMissing = false,
        EnrichmentWriteMode? writeMode = null,
        CancellationToken ct = default)
    {
        var query = _db.Works.Where(w => w.MediaType == MediaType.Anime);
        if (onlyMissing)
            query = query.Where(w => w.Overview == null || w.Overview == "");

        var ids = await query
            .OrderBy(w => w.UpdatedAt)
            .Select(w => w.Id)
            .Take(maxItems)
            .ToListAsync(ct);

        var enriched = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _service.EnrichAsync(id, forceRefresh, writeMode, ct);
            if (result.Applied.Count > 0)
                enriched++;

            if (delayMs > 0)
                await Task.Delay(delayMs, ct);
        }

        _log.LogInformation("Enriched {Enriched}/{Total} anime works (onlyMissing={OnlyMissing})",
            enriched, ids.Count, onlyMissing);
        return enriched;
    }
}
