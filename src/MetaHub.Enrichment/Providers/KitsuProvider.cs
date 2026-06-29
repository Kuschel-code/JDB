using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Kitsu provider (kitsu.app JSON:API, no API key). A fallback anime source that fills overview,
/// year, episode count and artwork for works whose only cross-id is a Kitsu id — manami links some
/// titles (e.g. "The Red Turtle") to Kitsu/anime-planet only, with no AniList/MAL/AniDB id, so the
/// richer providers have nothing to fetch. Lower priority than AniList/TMDB, so it only fills gaps.
/// </summary>
public class KitsuProvider : IMetadataProvider
{
    public const string HttpClientName = "kitsu";
    private const string ApiBase = "https://kitsu.app/api/edge/anime/";

    private readonly IHttpClientFactory _factory;

    public KitsuProvider(IHttpClientFactory factory) => _factory = factory;

    public ExternalIdSource Source => ExternalIdSource.Kitsu;

    // Fallback for anime: after AniList (10), Jikan and TMDB (15). Only wins a field nobody else fills.
    public int Priority => 18;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Kitsu)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return null;

        var client = _factory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + Uri.EscapeDataString(externalId));
        // Kitsu is a JSON:API service and expects this Accept media type.
        request.Headers.Accept.ParseAdd("application/vnd.api+json");

        var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);

        if (!doc.RootElement.TryGetProperty("data", out var node) || node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("attributes", out var a) || a.ValueKind != JsonValueKind.Object)
            return data;

        data.CanonicalTitle = GetString(a, "canonicalTitle");

        // titles: { en, en_jp (romaji), ja_jp (native) }
        if (a.TryGetProperty("titles", out var titles) && titles.ValueKind == JsonValueKind.Object)
        {
            var en = GetString(titles, "en");
            var romaji = GetString(titles, "en_jp");
            var native = GetString(titles, "ja_jp");
            data.OriginalTitle = native ?? romaji;
            data.CanonicalTitle ??= en ?? romaji;
            if (!string.IsNullOrWhiteSpace(en)) data.TitleTranslations["en"] = en;
            if (!string.IsNullOrWhiteSpace(native)) data.TitleTranslations["ja"] = native;
        }

        data.Overview = GetString(a, "synopsis");
        data.ReleaseYear = YearOf(GetString(a, "startDate"));

        if (a.TryGetProperty("episodeCount", out var ep) && ep.ValueKind == JsonValueKind.Number)
            data.EpisodeCount = ep.GetInt32();

        // Kitsu artwork is decent but not its strongest point: score below AniList (80) / TMDB (90)
        // so it only shows when nothing better exists.
        if (ImageUrl(a, "posterImage") is { } poster)
            data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = poster, Source = "kitsu", Score = 70 });
        if (ImageUrl(a, "coverImage") is { } cover)
            data.Images.Add(new NormalizedImage { Type = ImageType.Backdrop, Url = cover, Source = "kitsu", Score = 70 });

        return data;
    }

    private static string? ImageUrl(JsonElement attributes, string prop)
        => attributes.TryGetProperty(prop, out var img) && img.ValueKind == JsonValueKind.Object
            ? GetString(img, "original")
            : null;

    private static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
