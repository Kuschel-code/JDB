using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MetaHub.Domain.Entities;
using MetaHub.Enrichment.Providers;
using MetaHub.Infrastructure;

namespace MetaHub.Enrichment;

/// <summary>
/// Fills the episode table for an anime from Jikan's paged episode list
/// (<c>/v4/anime/{malId}/episodes</c>), so the Jellyfin episode provider has data to serve.
/// Titles prefer the Japanese original when the preferred language is "ja". Idempotent:
/// existing episodes are updated in place, keyed by (series, season 1, episode number).
/// </summary>
public class JikanEpisodeSync
{
    private const string BaseUrl = "https://api.jikan.moe/v4/anime/";
    private const int MaxPages = 30; // safety cap (~3000 episodes)

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<JikanEpisodeSync> _log;

    public JikanEpisodeSync(IHttpClientFactory factory, ILogger<JikanEpisodeSync> log)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>Fetches and upserts all episodes for <paramref name="work"/>. Returns the count written.</summary>
    public async Task<int> SyncAsync(
        MetaHubDbContext db, Work work, string malId, string? preferredLanguage, CancellationToken ct = default)
    {
        var client = _factory.CreateClient(JikanProvider.HttpClientName);
        var parsed = new List<ParsedEpisode>();

        for (int page = 1; page <= MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.GetAsync($"{BaseUrl}{malId}/episodes?page={page}", ct);
            if (!response.IsSuccessStatusCode)
                break;

            var (episodes, hasNext) = ParseEpisodesPage(await response.Content.ReadAsStringAsync(ct));
            parsed.AddRange(episodes);
            if (!hasNext)
                break;

            // Stay well within Jikan's rate limit (~3 req/s) between pages.
            await Task.Delay(400, ct);
        }

        if (parsed.Count == 0)
            return 0;

        // Episodes hang off SeriesDetail; make sure it exists.
        var detail = await db.SeriesDetails.FirstOrDefaultAsync(s => s.WorkId == work.Id, ct);
        if (detail is null)
        {
            detail = new SeriesDetail { WorkId = work.Id };
            db.SeriesDetails.Add(detail);
        }

        var existing = await db.Episodes
            .Where(e => e.SeriesWorkId == work.Id && e.SeasonNumber == 1)
            .ToDictionaryAsync(e => e.EpisodeNumber, ct);

        var preferJapanese = string.Equals(preferredLanguage, "ja", StringComparison.OrdinalIgnoreCase);
        foreach (var ep in parsed)
        {
            var title = preferJapanese && !string.IsNullOrWhiteSpace(ep.TitleJapanese) ? ep.TitleJapanese : ep.Title;
            if (existing.TryGetValue(ep.Number, out var row))
            {
                row.Title = title ?? row.Title;
                row.AirDate = ep.AirDate ?? row.AirDate;
                row.AbsoluteNumber = ep.Number;
            }
            else
            {
                db.Episodes.Add(new Episode
                {
                    SeriesWorkId = work.Id,
                    SeasonNumber = 1,
                    EpisodeNumber = ep.Number,
                    AbsoluteNumber = ep.Number,
                    Title = title,
                    AirDate = ep.AirDate
                });
            }
        }

        await db.SaveChangesAsync(ct);
        _log.LogInformation("Synced {Count} episodes for {Title} (MAL {MalId})",
            parsed.Count, work.CanonicalTitle, malId);
        return parsed.Count;
    }

    /// <summary>Parses one Jikan episodes page. Exposed for tests.</summary>
    public static (List<ParsedEpisode> Episodes, bool HasNextPage) ParseEpisodesPage(string json)
    {
        var episodes = new List<ParsedEpisode>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        bool hasNext = root.TryGetProperty("pagination", out var pagination)
                       && pagination.TryGetProperty("has_next_page", out var next)
                       && next.ValueKind == JsonValueKind.True;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in data.EnumerateArray())
            {
                if (!e.TryGetProperty("mal_id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                    continue;

                DateOnly? aired = null;
                if (GetString(e, "aired") is { Length: >= 10 } airedRaw &&
                    DateOnly.TryParse(airedRaw[..10], out var parsedDate))
                    aired = parsedDate;

                episodes.Add(new ParsedEpisode(
                    idEl.GetInt32(),
                    GetString(e, "title"),
                    GetString(e, "title_japanese"),
                    aired));
            }
        }

        return (episodes, hasNext);
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

public record ParsedEpisode(int Number, string? Title, string? TitleJapanese, DateOnly? AirDate);
