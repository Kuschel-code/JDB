using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>Movie metadata provider backed by the MetaHub API.</summary>
public class MetaHubMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubApiClient _client;

    public MetaHubMovieProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _client = new MetaHubApiClient(httpClientFactory);
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();
        var work = await MetaHubMapping.ResolveAsync(_client, info.ProviderIds, cancellationToken).ConfigureAwait(false);
        if (work is null)
            return result;

        result.HasMetadata = true;
        result.Item = new Movie
        {
            Name = work.CanonicalTitle,
            OriginalTitle = work.OriginalTitle,
            Overview = work.Overview,
            ProductionYear = work.ReleaseYear
        };
        MetaHubMapping.ApplyProviderIds(result.Item, work);
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
