using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Annict provider (annict.com) — the Japanese anime database/tracker. Supplies the Japanese
/// original title (and kana reading as a "ja" translation), season year, episode count and
/// artwork. Requires a personal access token (annict.com → settings → applications); inert
/// without one. Works are linked via the ARM mapping ingested alongside manami/Fribb.
/// </summary>
public class AnnictProvider : IMetadataProvider
{
    public const string HttpClientName = "annict";
    private const string Endpoint = "https://api.annict.com/graphql";

    private const string Query = @"
query ($ids: [Int!]) {
  searchWorks(annictIds: $ids) {
    nodes {
      title
      titleKana
      titleEn
      seasonYear
      episodesCount
      image { recommendedImageUrl facebookOgImageUrl }
    }
  }
}";

    private readonly IHttpClientFactory _factory;
    private readonly EnrichmentOptions _options;

    public AnnictProvider(IHttpClientFactory factory, IOptions<EnrichmentOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    public ExternalIdSource Source => ExternalIdSource.Annict;

    // Supplementary Japanese data; never outranks AniList/TMDB for shared fields.
    public int Priority => 30;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Annict)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AnnictToken) || !int.TryParse(externalId, out var id))
            return null;

        var client = _factory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query = Query, variables = new { ids = new[] { id } } })
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.AnnictToken}");

        var response = await client.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);

        if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
            !dataEl.TryGetProperty("searchWorks", out var search) ||
            !search.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
            return data;

        var node = nodes.EnumerateArray().FirstOrDefault();
        if (node.ValueKind != JsonValueKind.Object)
            return data;

        // "title" on Annict is the Japanese original.
        var japanese = GetString(node, "title");
        data.OriginalTitle = japanese;
        data.CanonicalTitle = GetString(node, "titleEn") ?? japanese;

        if (node.TryGetProperty("seasonYear", out var year) && year.ValueKind == JsonValueKind.Number)
            data.ReleaseYear = year.GetInt32();
        if (node.TryGetProperty("episodesCount", out var eps) && eps.ValueKind == JsonValueKind.Number && eps.GetInt32() > 0)
            data.EpisodeCount = eps.GetInt32();

        if (node.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
        {
            var url = GetString(image, "recommendedImageUrl") ?? GetString(image, "facebookOgImageUrl");
            if (!string.IsNullOrWhiteSpace(url))
                data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = url!, Source = "annict", Score = 55 });
        }

        return data;
    }

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
