using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>
/// Season metadata provider. Resolves the owning series through MetaHub and supplies a
/// localized season name, so MetaHub can be selected under "Metadata downloaders (Seasons)".
/// </summary>
public class MetaHubSeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
{
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubSeasonProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }

    public string Name => "MetaHub";

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Season>();
        var config = Plugin.Instance!.Configuration;

        // A season carries no usable provider ids of its own; resolve via the series.
        var series = await _backend.ResolveAsync(info.SeriesProviderIds, config.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);
        if (series is null || !_gate.IsServed(info, series.MediaType, config))
            return result;

        result.HasMetadata = true;
        result.Item = new Season
        {
            Name = SeasonName(info.IndexNumber, config.PreferredLanguage),
            IndexNumber = info.IndexNumber
        };
        return result;
    }

    public static string SeasonName(int? number, string? lang)
    {
        var german = string.Equals(lang, "de", StringComparison.OrdinalIgnoreCase);
        if (number is null or 0)
            return german ? "Extras" : "Specials";
        return german ? $"Staffel {number}" : $"Season {number}";
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
