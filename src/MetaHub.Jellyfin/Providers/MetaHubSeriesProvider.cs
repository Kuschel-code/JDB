using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>Series/anime metadata provider backed by the MetaHub API.</summary>
public class MetaHubSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubApiClient _client;

    public MetaHubSeriesProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _client = new MetaHubApiClient(httpClientFactory);
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();
        var work = await MetaHubMapping.ResolveAsync(_client, info.ProviderIds, cancellationToken).ConfigureAwait(false);
        if (work is null)
            return result;

        result.HasMetadata = true;
        result.Item = new Series
        {
            Name = work.CanonicalTitle,
            OriginalTitle = work.OriginalTitle,
            Overview = work.Overview,
            ProductionYear = work.ReleaseYear
        };
        MetaHubMapping.ApplyProviderIds(result.Item, work);
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
