using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Open Library provider for books (free, no key). Resolves by Open Library id or ISBN.
/// </summary>
public class OpenLibraryProvider : IMetadataProvider
{
    public const string HttpClientName = "openlibrary";

    private readonly IHttpClientFactory _factory;

    public OpenLibraryProvider(IHttpClientFactory factory) => _factory = factory;

    public ExternalIdSource Source => ExternalIdSource.OpenLibrary;
    public int Priority => 10;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.OpenLibrary)?.ExternalValue
           ?? work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Isbn)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        // OLIDs look like "OL...M"; otherwise treat the value as an ISBN.
        var url = externalId.StartsWith("OL", StringComparison.OrdinalIgnoreCase)
            ? $"https://openlibrary.org/books/{externalId}.json"
            : $"https://openlibrary.org/isbn/{externalId}.json";

        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);
        var d = doc.RootElement;

        data.CanonicalTitle = GetString(d, "title");
        data.ReleaseYear = YearOf(GetString(d, "publish_date"));

        if (d.TryGetProperty("number_of_pages", out var pages) && pages.ValueKind == JsonValueKind.Number)
            data.PageCount = pages.GetInt32();

        if (d.TryGetProperty("publishers", out var pubs) && pubs.ValueKind == JsonValueKind.Array)
            data.Publisher = pubs.EnumerateArray().FirstOrDefault().GetString();

        if (d.TryGetProperty("isbn_13", out var isbns) && isbns.ValueKind == JsonValueKind.Array)
            data.Isbn13 = isbns.EnumerateArray().FirstOrDefault().GetString();

        if (d.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
        {
            var cover = covers.EnumerateArray().FirstOrDefault();
            if (cover.ValueKind == JsonValueKind.Number)
                data.Images.Add(new NormalizedImage
                {
                    Type = ImageType.Cover,
                    Url = $"https://covers.openlibrary.org/b/id/{cover.GetInt32()}-L.jpg",
                    Source = "openlibrary",
                    Score = 70
                });
        }

        return data;
    }

    private static int? YearOf(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var digits = new string(date.Where(char.IsDigit).ToArray());
        // Publish dates vary wildly ("2001", "Jan 01, 2001"); take a 4-digit year if present.
        for (int i = 0; i + 4 <= digits.Length; i++)
            if (int.TryParse(digits.AsSpan(i, 4), out var y) && y is >= 1400 and <= 2100)
                return y;
        return null;
    }

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
