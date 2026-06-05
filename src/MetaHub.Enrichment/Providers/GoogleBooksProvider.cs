using System.Text.Json;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Google Books provider for books (key optional). Complements Open Library, so it has lower
/// precedence. Resolves by Google volume id or ISBN.
/// </summary>
public class GoogleBooksProvider : IMetadataProvider
{
    public const string HttpClientName = "googlebooks";

    private readonly IHttpClientFactory _factory;
    private readonly EnrichmentOptions _options;

    public GoogleBooksProvider(IHttpClientFactory factory, IOptions<EnrichmentOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    public ExternalIdSource Source => ExternalIdSource.GoogleBooks;
    public int Priority => 20;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.GoogleBooks)?.ExternalValue
           ?? work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Isbn)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        var keyParam = string.IsNullOrWhiteSpace(_options.GoogleBooksApiKey)
            ? string.Empty : $"&key={_options.GoogleBooksApiKey}";

        // Heuristic: ISBNs are all digits (optionally with a trailing X); otherwise a volume id.
        var isIsbn = externalId.All(c => char.IsDigit(c) || c is 'X' or 'x');
        var url = isIsbn
            ? $"https://www.googleapis.com/books/v1/volumes?q=isbn:{externalId}{keyParam}"
            : $"https://www.googleapis.com/books/v1/volumes/{externalId}?{keyParam.TrimStart('&')}";

        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        // Search responses wrap the volume under items[0]; direct lookups return it at the root.
        JsonElement volume = root;
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var first = items.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return data;
            volume = first;
        }

        if (!volume.TryGetProperty("volumeInfo", out var info) || info.ValueKind != JsonValueKind.Object)
            return data;

        data.CanonicalTitle = GetString(info, "title");
        data.Overview = GetString(info, "description");
        data.ReleaseYear = YearOf(GetString(info, "publishedDate"));
        data.Publisher = GetString(info, "publisher");

        if (info.TryGetProperty("pageCount", out var pages) && pages.ValueKind == JsonValueKind.Number)
            data.PageCount = pages.GetInt32();

        if (info.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
            foreach (var c in cats.EnumerateArray())
                if (c.GetString() is { } name) data.Genres.Add(name);

        if (info.TryGetProperty("imageLinks", out var links) && links.ValueKind == JsonValueKind.Object)
        {
            var url = GetString(links, "thumbnail") ?? GetString(links, "smallThumbnail");
            if (url is not null)
                data.Images.Add(new NormalizedImage { Type = ImageType.Cover, Url = url, Source = "googlebooks", Score = 50 });
        }

        return data;
    }

    private static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
