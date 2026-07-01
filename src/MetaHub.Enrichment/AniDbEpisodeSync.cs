using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Identification.AniDb;
using MetaHub.Infrastructure;

namespace MetaHub.Enrichment;

/// <summary>
/// Fills the episode table for an anime from AniDB's HTTP anime data (cached as JSON in
/// <see cref="RawPayload"/>). Populates AniDB-specific fields: <see cref="Episode.AniDbEpisodeId"/>,
/// <see cref="Episode.Kind"/>, <see cref="Episode.RawEpno"/>. Idempotent: existing episodes are
/// updated in place, keyed by (series, season, episode number).
/// </summary>
public class AniDbEpisodeSync
{
    private readonly ILogger<AniDbEpisodeSync> _log;

    public AniDbEpisodeSync(ILogger<AniDbEpisodeSync> log) => _log = log;

    public async Task<int> SyncAsync(
        MetaHubDbContext db, Work work, string aidString, string? preferredLanguage, CancellationToken ct = default)
    {
        var body = await db.RawPayloads
            .Where(p => p.WorkId == work.Id && p.Source == ExternalIdSource.AniDb && p.HttpStatus == 200)
            .OrderByDescending(p => p.FetchedAt)
            .Select(p => p.Body)
            .FirstOrDefaultAsync(ct);

        if (body is null)
            return 0;

        AniDbAnime? anime;
        try { anime = JsonSerializer.Deserialize<AniDbAnime>(body); }
        catch (JsonException) { return 0; }

        if (anime is null || anime.Episodes.Count == 0)
            return 0;

        var detail = await db.SeriesDetails.FirstOrDefaultAsync(s => s.WorkId == work.Id, ct);
        if (detail is null)
        {
            detail = new SeriesDetail { WorkId = work.Id };
            db.SeriesDetails.Add(detail);
        }

        var existing = await db.Episodes
            .Where(e => e.SeriesWorkId == work.Id)
            .ToDictionaryAsync(e => (e.SeasonNumber, e.EpisodeNumber, e.Kind), ct);

        var preferJapanese = string.Equals(preferredLanguage, "ja", StringComparison.OrdinalIgnoreCase);
        var count = 0;

        foreach (var ep in anime.Episodes)
        {
            var kind = ep.Kind;
            var number = ep.ParsedNumber;
            if (number <= 0) continue;

            var season = kind == EpisodeKind.Regular ? 1 : 0;
            var title = preferJapanese && !string.IsNullOrWhiteSpace(ep.Title)
                ? ep.Title
                : (ep.TitleEn ?? ep.Title);

            var key = (season, number, kind);
            if (existing.TryGetValue(key, out var row))
            {
                row.Title = title ?? row.Title;
                row.AirDate = ep.AirDate ?? row.AirDate;
                row.AniDbEpisodeId = ep.Id;
                row.Kind = kind;
                row.RawEpno = ep.RawEpno;
                row.AbsoluteNumber ??= kind == EpisodeKind.Regular ? number : null;
            }
            else
            {
                var entity = new Episode
                {
                    SeriesWorkId = work.Id,
                    SeasonNumber = season,
                    EpisodeNumber = number,
                    AbsoluteNumber = kind == EpisodeKind.Regular ? number : null,
                    Title = title,
                    AirDate = ep.AirDate,
                    AniDbEpisodeId = ep.Id,
                    Kind = kind,
                    RawEpno = ep.RawEpno
                };
                db.Episodes.Add(entity);
                existing[key] = entity;
            }

            count++;
        }

        await db.SaveChangesAsync(ct);
        _log.LogInformation("AniDB: synced {Count} episodes for {Title} (aid {Aid})",
            count, work.CanonicalTitle, aidString);
        return count;
    }
}
