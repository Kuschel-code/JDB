using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>
/// Supplies artwork for movies, series and seasons, backed by the embedded engine or a remote
/// API. Seasons fall back to the owning series' artwork (poster/backdrop), the common
/// convention when no season-specific art exists.
/// </summary>
public class MetaHubImageProvider : IRemoteImageProvider
{
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubImageProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }

    public string Name => "MetaHub";

    public bool Supports(BaseItem item) => item is Series or Movie or Season or Episode;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => item switch
    {
        Season => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb },
        Episode => new[] { ImageType.Primary, ImageType.Thumb },
        _ => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Logo, ImageType.Thumb }
    };

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;

        // Episode stills are not stored yet (future: TMDB/AniDB episode images); MetaHub is
        // listed under the episode image fetchers but currently supplies nothing for them.
        if (item is Episode)
            return Enumerable.Empty<RemoteImageInfo>();

        // Seasons have no provider ids of their own — use the owning series'.
        var providerIds = item is Season { Series: { } parent } ? parent.ProviderIds : item.ProviderIds;

        var work = await _backend.ResolveAsync(providerIds, config.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);
        if (work is null || !_gate.IsServed(item, work.MediaType, config))
            return Enumerable.Empty<RemoteImageInfo>();

        var images = await _backend.GetImagesAsync(work.Id, cancellationToken).ConfigureAwait(false);
        var result = images.Select(img => new RemoteImageInfo
        {
            ProviderName = Name,
            Url = img.Url,
            Type = MetaHubMapping.ToJellyfinImageType(img.Type),
            Width = img.Width,
            Height = img.Height,
            Language = img.Lang
        });

        // For seasons only image types Jellyfin accepts on a season make sense.
        if (item is Season)
            result = result.Where(i => i.Type is ImageType.Primary or ImageType.Backdrop or ImageType.Thumb);

        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
