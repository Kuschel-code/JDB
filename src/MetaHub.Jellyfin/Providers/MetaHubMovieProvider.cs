using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>Movie metadata provider, backed by the embedded engine or a remote API.</summary>
public class MetaHubMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubMovieProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();
        var config = Plugin.Instance!.Configuration;
        var work = await _backend.ResolveAsync(info.ProviderIds, config.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);
        if (work is null || !_gate.IsServed(info, work.MediaType, config))
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
