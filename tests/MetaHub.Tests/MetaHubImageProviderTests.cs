using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MetaHub.Jellyfin.Providers;
using Xunit;

namespace MetaHub.Tests;

public class MetaHubImageProviderTests
{
    private static MetaHubImageProvider NewProvider() => new(backend: null!, httpClientFactory: null!, gate: null!);

    [Fact]
    public void Does_not_advertise_episode_support()
    {
        // MetaHub does not fetch episode stills; advertising Episode support here would list it
        // as a selectable "Episode image" fetcher in Jellyfin's UI that silently does nothing —
        // GetImages already special-cased this away, Supports must agree.
        Assert.False(NewProvider().Supports(new Episode()));
    }

    [Theory]
    [InlineData(typeof(Series))]
    [InlineData(typeof(Movie))]
    [InlineData(typeof(Season))]
    public void Advertises_support_for_types_it_actually_serves(Type itemType)
    {
        var item = (MediaBrowser.Controller.Entities.BaseItem)Activator.CreateInstance(itemType)!;
        Assert.True(NewProvider().Supports(item));
    }

    [Fact]
    public void Season_image_types_exclude_banner_and_logo()
    {
        var types = NewProvider().GetSupportedImages(new Season()).ToList();
        Assert.DoesNotContain(ImageType.Banner, types);
        Assert.DoesNotContain(ImageType.Logo, types);
    }
}
