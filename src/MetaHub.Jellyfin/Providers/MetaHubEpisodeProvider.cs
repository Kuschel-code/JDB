using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>
/// Episode metadata provider. Resolves the owning series through MetaHub and serves the
/// episode (title, overview, air date) from the local episode table — filled by the
/// enrichment task. Falls back to absolute numbering, which anime libraries commonly use.
/// </summary>
public class MetaHubEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
{
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubEpisodeProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>();
        var config = Plugin.Instance!.Configuration;

        var episode = await _backend.GetEpisodeAsync(
                info.SeriesProviderIds, info.ParentIndexNumber, info.IndexNumber, cancellationToken)
            .ConfigureAwait(false);
        if (episode is null || !_gate.IsServed(info, episode.SeriesMediaType, config))
            return result;

        result.HasMetadata = true;
        result.Item = new Episode
        {
            Name = string.IsNullOrWhiteSpace(episode.Title) ? $"Episode {episode.EpisodeNumber}" : episode.Title,
            Overview = episode.Overview,
            IndexNumber = info.IndexNumber ?? episode.EpisodeNumber,
            ParentIndexNumber = info.ParentIndexNumber ?? episode.SeasonNumber
        };
        if (episode.AirDate is { } air)
        {
            result.Item.PremiereDate = air.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            result.Item.ProductionYear = air.Year;
        }

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
