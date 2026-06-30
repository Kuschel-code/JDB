using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;

namespace MetaHub.Enrichment;

/// <summary>
/// Orchestrates enrichment for a work: for each applicable provider it serves a cached
/// <see cref="RawPayload"/> while within TTL, otherwise fetches fresh (Polly-protected),
/// stores the raw body and updates rate-limit bookkeeping. All providers' normalized data is
/// then merged into the canonical work by priority.
/// </summary>
public class EnrichmentService
{
    private readonly MetaHubDbContext _db;
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly EnrichmentOptions _options;
    private readonly ILogger<EnrichmentService> _log;

    private readonly JikanEpisodeSync? _episodeSync;

    public EnrichmentService(
        MetaHubDbContext db,
        IEnumerable<IMetadataProvider> providers,
        IOptions<EnrichmentOptions> options,
        ILogger<EnrichmentService> log,
        JikanEpisodeSync? episodeSync = null)
    {
        _db = db;
        _options = options.Value;
        // Honor per-source on/off toggles: drop providers the user disabled.
        _providers = providers
            .Where(p => !_options.DisabledSources.Contains(p.Source.ToString()))
            .OrderBy(p => p.Priority)
            .ToList();
        _log = log;
        _episodeSync = episodeSync;
    }

    public async Task<EnrichmentResult> EnrichAsync(
        Guid workId,
        bool forceRefresh = false,
        EnrichmentWriteMode? writeMode = null,
        CancellationToken ct = default)
    {
        var work = await _db.Works
            .Include(w => w.ExternalIds)
            .Include(w => w.Images)
            .Include(w => w.SeriesDetail)
            .Include(w => w.MusicDetail)
            .Include(w => w.BookDetail)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);

        if (work is null)
            return new EnrichmentResult { WorkId = workId, Found = false };

        var ttl = TimeSpan.FromDays(work.Status == WorkStatus.Ongoing
            ? _options.TtlOngoingDays
            : _options.TtlFinishedDays);

        var collected = new List<NormalizedWorkData>();
        var result = new EnrichmentResult { WorkId = workId, Found = true };

        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();

            var externalId = provider.GetExternalId(work);
            if (externalId is null)
                continue;

            string? body = forceRefresh ? null : await GetCachedBodyAsync(workId, provider.Source, ttl, ct);
            bool fromCache = body is not null;

            if (body is null)
            {
                body = await provider.FetchRawAsync(work, externalId, ct);
                if (body is null)
                {
                    result.Misses.Add(provider.Source.ToString());
                    continue;
                }

                await StoreRawAsync(workId, provider.Source, body, ct);
                await UpdateFetchLogAsync(provider.Source, ct);
            }

            try
            {
                collected.Add(provider.Parse(body));
                result.Applied.Add($"{provider.Source}{(fromCache ? " (cached)" : "")}");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to parse {Source} payload for work {WorkId}", provider.Source, workId);
            }
        }

        if (collected.Count > 0)
        {
            await new WorkMerger(_db).ApplyAsync(
                work, collected, writeMode ?? _options.WriteMode, _options.PreferredLanguage, ct);
            await _db.SaveChangesAsync(ct);
        }

        await SyncEpisodesIfMissingAsync(work, forceRefresh, ct);

        return result;
    }

    /// <summary>
    /// Best-effort: fill the episode table for anime via Jikan when episodes are missing
    /// (or stale relative to the known episode count). Failures never break enrichment.
    /// </summary>
    private async Task SyncEpisodesIfMissingAsync(Work work, bool force, CancellationToken ct)
    {
        if (_episodeSync is null || work.MediaType != MediaType.Anime)
            return;

        var malId = work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Mal)?.ExternalValue;
        if (string.IsNullOrWhiteSpace(malId))
            return;

        try
        {
            var have = await _db.Episodes.CountAsync(e => e.SeriesWorkId == work.Id, ct);
            var expected = work.SeriesDetail?.EpisodeCount ?? 0;
            if (!force && have > 0 && (expected == 0 || have >= expected))
                return;

            await _episodeSync.SyncAsync(_db, work, malId!, _options.PreferredLanguage, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Episode sync failed for work {WorkId}", work.Id);
        }
    }

    private async Task<string?> GetCachedBodyAsync(Guid workId, ExternalIdSource source, TimeSpan ttl, CancellationToken ct)
    {
        var threshold = DateTimeOffset.UtcNow - ttl;
        return await _db.RawPayloads
            .Where(p => p.WorkId == workId && p.Source == source && p.FetchedAt >= threshold && p.HttpStatus == 200)
            .OrderByDescending(p => p.FetchedAt)
            .Select(p => p.Body)
            .FirstOrDefaultAsync(ct);
    }

    private async Task StoreRawAsync(Guid workId, ExternalIdSource source, string body, CancellationToken ct)
    {
        _db.RawPayloads.Add(new RawPayload
        {
            WorkId = workId,
            Source = source,
            HttpStatus = 200,
            Body = body
        });
        await Task.CompletedTask;
    }

    private async Task UpdateFetchLogAsync(ExternalIdSource source, CancellationToken ct)
    {
        var log = await _db.SourceFetchLogs.FirstOrDefaultAsync(l => l.Source == source, ct);
        if (log is null)
        {
            log = new SourceFetchLog { Source = source, CallsInWindow = 0 };
            _db.SourceFetchLogs.Add(log);
        }

        log.LastCallAt = DateTimeOffset.UtcNow;
        log.CallsInWindow++;
    }
}

public class EnrichmentResult
{
    public Guid WorkId { get; init; }
    public bool Found { get; init; }
    public List<string> Applied { get; } = new();
    public List<string> Misses { get; } = new();
}
