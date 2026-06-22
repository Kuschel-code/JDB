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
        var work = await _backend.ResolveAsync(info.ProviderIds, config.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);

        // No provider id matched — fall back to title matching (lookup name, then folder name),
        // biased by the library's media type so e.g. "227" finds the anime "22/7" in an Anime library.
        var preferredType = _classifier.Classify(info, config);
        if (work is null && config.MatchByName)
            work = await _backend.ResolveByNameAsync(
                MetaHubMapping.NameCandidates(info), info.Year, preferredType,
                MetaHubMapping.FolderTitle(info.Path), config.PreferredLanguage, cancellationToken)
                .ConfigureAwait(false);

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
        MetaHubMapping.ApplyProviderIds(result.Item, work);
        MetaHubMapping.ApplyPeople(result, work);
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
