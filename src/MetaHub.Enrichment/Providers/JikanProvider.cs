using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Jikan provider (unofficial MyAnimeList API, no key). Used to complement AniList, so it has
/// a higher priority number (lower precedence) for overlapping fields.
/// </summary>
public class JikanProvider : IMetadataProvider
{
    public const string HttpClientName = "jikan";
    private const string BaseUrl = "https://api.jikan.moe/v4/anime/";

    private readonly IHttpClientFactory _factory;

    public JikanProvider(IHttpClientFactory factory) => _factory = factory;

    public ExternalIdSource Source => ExternalIdSource.Mal;
    public int Priority => 20;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Mal)?.ExternalValue;

    public async Task<string?> FetchRawAsync(string externalId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalId, out var id))
            return null;

        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync($"{BaseUrl}{id}/full", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var result = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);

        if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object)
            return result;

        result.CanonicalTitle = GetString(d, "title_english") ?? GetString(d, "title");
        result.OriginalTitle = GetString(d, "title_japanese");
        result.Overview = GetString(d, "synopsis");

        if (d.TryGetProperty("year", out var year) && year.ValueKind == JsonValueKind.Number)
            result.ReleaseYear = year.GetInt32();

        result.Status = MapStatus(GetString(d, "status"));

        if (d.TryGetProperty("episodes", out var eps) && eps.ValueKind == JsonValueKind.Number)
            result.EpisodeCount = eps.GetInt32();

        foreach (var key in new[] { "genres", "themes", "demographics" })
            if (d.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var g in arr.EnumerateArray())
                    if (GetString(g, "name") is { } name) result.Genres.Add(name);

        // images.jpg.large_image_url
        if (d.TryGetProperty("images", out var images) &&
            images.TryGetProperty("jpg", out var jpg) &&
            GetString(jpg, "large_image_url") is { } poster)
            result.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = poster, Source = "mal", Score = 60 });

        return result;
    }

    private static WorkStatus MapStatus(string? status) => status switch
    {
        "Finished Airing" => WorkStatus.Finished,
        "Currently Airing" => WorkStatus.Ongoing,
        "Not yet aired" => WorkStatus.Announced,
        _ => WorkStatus.Unknown
    };

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
