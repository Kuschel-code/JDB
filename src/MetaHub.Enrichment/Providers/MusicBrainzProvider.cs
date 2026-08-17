using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// MusicBrainz provider for albums/releases (no key, but a descriptive User-Agent and ~1 req/s
/// are required — enforced by <see cref="MusicBrainzRateLimiter"/> across every caller).
/// </summary>
public class MusicBrainzProvider : IMetadataProvider
{
    public const string HttpClientName = "musicbrainz";

    private readonly IHttpClientFactory _factory;
    private readonly MusicBrainzRateLimiter _rateLimiter;

    public MusicBrainzProvider(IHttpClientFactory factory, MusicBrainzRateLimiter rateLimiter)
    {
        _factory = factory;
        _rateLimiter = rateLimiter;
    }

    public ExternalIdSource Source => ExternalIdSource.MusicBrainz;
    public int Priority => 10;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.MusicBrainz)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct);

        var url = $"https://musicbrainz.org/ws/2/release/{externalId}?fmt=json&inc=labels+recordings+media";
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
        data.ReleaseYear = YearOf(GetString(d, "date"));

        if (d.TryGetProperty("label-info", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            var first = labels.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("label", out var label))
                data.Label = GetString(label, "name");
        }

        if (d.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
        {
            var tracks = 0;
            foreach (var m in media.EnumerateArray())
                if (m.TryGetProperty("track-count", out var tc) && tc.ValueKind == JsonValueKind.Number)
                    tracks += tc.GetInt32();
            if (tracks > 0) data.TrackCount = tracks;
        }

        return data;
    }

    private static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
