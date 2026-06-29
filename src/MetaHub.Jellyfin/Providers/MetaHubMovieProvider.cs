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
    private readonly MetaHubLibraryClassifier _classifier;

    public MetaHubMovieProvider(
        IMetaHubBackend backend, IHttpClientFactory httpClientFactory,
        MetaHubItemGate gate, MetaHubLibraryClassifier classifier)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
        _classifier = classifier;
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();
        var config = Plugin.Instance!.Configuration;
        var preferredType = _classifier.Classify(info, config);

        Task<WorkDto?> ById() =>
            _backend.ResolveAsync(info.ProviderIds, config.PreferredLanguage, cancellationToken);
        Task<WorkDto?> ByName() => config.MatchByName
            ? _backend.ResolveByNameAsync(
                MetaHubMapping.NameCandidates(info), info.Year, preferredType,
                MetaHubMapping.FolderTitle(info.Path), config.PreferredLanguage, cancellationToken)
            : Task.FromResult<WorkDto?>(null);

        // Default: provider ids first, then the folder/name fallback. With PreferNameOverIds the
        // folder name wins (for libraries with wrong/stale ids baked into the files).
        WorkDto? work = config.PreferNameOverIds
            ? await ByName().ConfigureAwait(false) ?? await ById().ConfigureAwait(false)
            : await ById().ConfigureAwait(false) ?? await ByName().ConfigureAwait(false);

        if (work is null)
        {
            // Last resort: supply the cleaned file/folder name so titles are never empty.
            var folderTitle = config.MatchByName ? MetaHubMapping.FolderTitle(info.Path) : null;
            if (!string.IsNullOrWhiteSpace(folderTitle) && _gate.IsServed(info, "Movie", config))
            {
                result.HasMetadata = true;
                result.Item = new Movie { Name = folderTitle, ProductionYear = info.Year };
            }
            return result;
        }

        if (!_gate.IsServed(info, work.MediaType, config))
            return result;

        result.HasMetadata = true;
        result.Item = new Movie
        {
            Name = work.CanonicalTitle,
            OriginalTitle = work.OriginalTitle,
            Overview = work.Overview,
            ProductionYear = work.ReleaseYear
        };
        if (work.Genres.Count > 0)
            result.Item.Genres = work.Genres.ToArray();
        MetaHubMapping.ApplyProviderIds(result.Item, work);
        MetaHubMapping.ApplyPeople(result, work);
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
