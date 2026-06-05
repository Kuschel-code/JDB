using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>Supplies artwork for movies and series, backed by the embedded engine or a remote API.</summary>
public class MetaHubImageProvider : IRemoteImageProvider
{
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;

    public MetaHubImageProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "MetaHub";

    public bool Supports(BaseItem item) => item is Series or Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[]
    {
        ImageType.Primary,
        ImageType.Backdrop,
        ImageType.Banner,
        ImageType.Logo,
        ImageType.Thumb
    };

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var work = await _backend.ResolveAsync(item.ProviderIds, config.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);
        if (work is null || !MetaHubMapping.IsMediaTypeEnabled(work.MediaType, config))
            return Enumerable.Empty<RemoteImageInfo>();

        var images = await _backend.GetImagesAsync(work.Id, cancellationToken).ConfigureAwait(false);
        return images.Select(img => new RemoteImageInfo
        {
            ProviderName = Name,
            Url = img.Url,
            Type = MetaHubMapping.ToJellyfinImageType(img.Type),
            Width = img.Width,
            Height = img.Height,
            Language = img.Lang
        });
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(nameof(MetaHubApiClient)).GetAsync(url, cancellationToken);
}
