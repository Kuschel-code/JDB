using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Providers;

/// <summary>Supplies artwork for movies and series from the MetaHub API.</summary>
public class MetaHubImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubApiClient _client;

    public MetaHubImageProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _client = new MetaHubApiClient(httpClientFactory);
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
        var work = await MetaHubMapping.ResolveAsync(_client, item.ProviderIds, cancellationToken).ConfigureAwait(false);
        if (work is null)
            return Enumerable.Empty<RemoteImageInfo>();

        var images = await _client.GetImagesAsync(work.Id, cancellationToken).ConfigureAwait(false);
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
