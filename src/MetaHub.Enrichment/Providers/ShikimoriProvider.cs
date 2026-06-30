using System.Text.Json;
using System.Text.RegularExpressions;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Shikimori (shikimori.one) provider — no API key, keyed by the MyAnimeList id every anime work
/// already carries. A low-priority gap-filler: contributes a poster, genres and episode count, plus
/// the Russian synopsis as a translation (never the default overview, since the viewer is unlikely
/// to read Russian). Useful for entries the richer providers do not cover well.
/// </summary>
public partial class ShikimoriProvider : IMetadataProvider
{
    public const string HttpClientName = "shikimori";
    private const string Host = "https://shikimori.one";

    private readonly IHttpClientFactory _factory;

    public ShikimoriProvider(IHttpClientFactory factory) => _factory = factory;

    public ExternalIdSource Source => ExternalIdSource.Shikimori;

    // Gap-fill: after AniList (10), Jikan, TMDB (15) and Kitsu (18).
    public int Priority => 22;

    // Shikimori is keyed by the MyAnimeList id.
    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Mal)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalId, out _))
            return null;

        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync($"{Host}/api/animes/{externalId}", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);
        var d = doc.RootElement;
        if (d.ValueKind != JsonValueKind.Object)
            return data;

        data.CanonicalTitle = GetString(d, "name");
        if (d.TryGetProperty("japanese", out var jp) && jp.ValueKind == JsonValueKind.Array)
            foreach (var j in jp.EnumerateArray())
                if (j.ValueKind == JsonValueKind.String) { data.OriginalTitle = j.GetString(); break; }

        // Russian synopsis -> translation only (strip Shikimori BBCode like [character=..]..[/character]).
        if (GetString(d, "description") is { Length: > 0 } desc)
            data.OverviewTranslations["ru"] = BbCode().Replace(desc, string.Empty).Trim();

        data.ReleaseYear = YearOf(GetString(d, "aired_on"));

        if (d.TryGetProperty("episodes", out var ep) && ep.ValueKind == JsonValueKind.Number && ep.GetInt32() > 0)
            data.EpisodeCount = ep.GetInt32();

        if (d.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            foreach (var g in genres.EnumerateArray())
                if (GetString(g, "name") is { } gn) data.Genres.Add(gn);

        // image paths are host-relative ("/system/animes/original/25537.jpg?...").
        if (d.TryGetProperty("image", out var img) && GetString(img, "original") is { } relative)
            data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = Host + relative, Source = "shikimori", Score = 68 });

        return data;
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex BbCode();

    private static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
