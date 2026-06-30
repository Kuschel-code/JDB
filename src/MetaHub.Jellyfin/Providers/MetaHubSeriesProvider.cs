using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>Series/anime metadata provider, backed by the embedded engine or a remote API.</summary>
public class MetaHubSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
{
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;
    private readonly MetaHubLibraryClassifier _classifier;

    public MetaHubSeriesProvider(
        IMetaHubBackend backend, IHttpClientFactory httpClientFactory,
        MetaHubItemGate gate, MetaHubLibraryClassifier classifier)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
        _classifier = classifier;
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();
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
            // Last resort (user request): when nothing matches, at least supply the folder
            // name as the title so items never keep empty/garbage names.
            var folderTitle = config.MatchByName ? MetaHubMapping.FolderTitle(info.Path) : null;
            if (!string.IsNullOrWhiteSpace(folderTitle) && _gate.IsServed(info, "Series", config))
            {
                result.HasMetadata = true;
                result.Item = new Series { Name = folderTitle, ProductionYear = info.Year };
            }
            return result;
        }

        if (!_gate.IsServed(info, work.MediaType, config))
            return result;

        result.HasMetadata = true;
        result.Item = new Series
        {
            Name = work.CanonicalTitle,
            OriginalTitle = work.OriginalTitle,
            Overview = work.Overview,
            ProductionYear = work.ReleaseYear
        };
        if (work.Genres.Count > 0)
            result.Item.Genres = work.Genres.ToArray();
        if (work.Studios.Count > 0)
            result.Item.Studios = work.Studios.ToArray();
        MetaHubMapping.ApplyProviderIds(result.Item, work);
        MetaHubMapping.ApplyPeople(result, work);
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
