using System.Net.Http.Json;
using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// AniList provider (GraphQL, no API key, generous limits). Most authoritative for anime,
/// so it has the lowest priority number.
/// </summary>
public class AniListProvider : IMetadataProvider
{
    public const string HttpClientName = "anilist";
    private const string Endpoint = "https://graphql.anilist.co";

    private const string Query = @"
query ($id: Int) {
  Media(id: $id, type: ANIME) {
    title { romaji english native }
    description(asHtml: false)
    status
    startDate { year }
    episodes
    genres
    coverImage { extraLarge color }
    bannerImage
  }
}";

    private readonly IHttpClientFactory _factory;

    public AniListProvider(IHttpClientFactory factory) => _factory = factory;

    public ExternalIdSource Source => ExternalIdSource.AniList;
    public int Priority => 10;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.AniList)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalId, out var id))
            return null;

        var client = _factory.CreateClient(HttpClientName);
        var request = new { query = Query, variables = new { id } };
        var response = await client.PostAsJsonAsync(Endpoint, request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);

        if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
            !dataEl.TryGetProperty("Media", out var media) ||
            media.ValueKind != JsonValueKind.Object)
            return data;

        if (media.TryGetProperty("title", out var title))
        {
            var english = GetString(title, "english");
            var romaji = GetString(title, "romaji");
            data.CanonicalTitle = english ?? romaji;
            data.OriginalTitle = GetString(title, "native") ?? romaji;
        }

        var description = GetString(media, "description");
        if (description is not null)
        {
            // AniList descriptions may contain simple HTML line breaks.
            data.Overview = description.Replace("<br>", "\n").Replace("<br/>", "\n").Trim();
        }

        if (media.TryGetProperty("startDate", out var start) && start.TryGetProperty("year", out var year)
            && year.ValueKind == JsonValueKind.Number)
            data.ReleaseYear = year.GetInt32();

        data.Status = MapStatus(GetString(media, "status"));

        if (media.TryGetProperty("episodes", out var eps) && eps.ValueKind == JsonValueKind.Number)
            data.EpisodeCount = eps.GetInt32();

        if (media.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            foreach (var g in genres.EnumerateArray())
                if (g.GetString() is { } name) data.Genres.Add(name);

        if (media.TryGetProperty("coverImage", out var cover) && GetString(cover, "extraLarge") is { } poster)
            data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = poster, Source = "anilist", Score = 80 });

        if (GetString(media, "bannerImage") is { } banner)
            data.Images.Add(new NormalizedImage { Type = ImageType.Backdrop, Url = banner, Source = "anilist", Score = 80 });

        return data;
    }

    private static WorkStatus MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "FINISHED" => WorkStatus.Finished,
        "RELEASING" => WorkStatus.Ongoing,
        "NOT_YET_RELEASED" => WorkStatus.Announced,
        "CANCELLED" => WorkStatus.Cancelled,
        "HIATUS" => WorkStatus.OnHiatus,
        _ => WorkStatus.Unknown
    };

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
