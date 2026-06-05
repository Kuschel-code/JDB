using Microsoft.Extensions.Options;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// Downloads the mapping datasets over HTTP. Resilience (retry/backoff) is supplied by
/// the Polly handler configured on the named <see cref="HttpClient"/>.
/// </summary>
public class HttpDatasetSource : IDatasetSource
{
    public const string HttpClientName = "anime-datasets";

    private readonly IHttpClientFactory _factory;
    private readonly AnimeIngestOptions _options;

    public HttpDatasetSource(IHttpClientFactory factory, IOptions<AnimeIngestOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    public Task<Stream> OpenManamiAsync(CancellationToken ct = default) => OpenAsync(_options.ManamiUrl, ct);

    public Task<Stream> OpenFribbAsync(CancellationToken ct = default) => OpenAsync(_options.FribbUrl, ct);

    private async Task<Stream> OpenAsync(string url, CancellationToken ct)
    {
        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
}
