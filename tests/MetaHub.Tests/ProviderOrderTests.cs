using MediaBrowser.Controller.Providers;
using MetaHub.Jellyfin.Providers;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Before this, none of MetaHub's providers implemented IHasOrder, so their position relative to
/// TheMovieDb/TheTVDB in a freshly added library's provider order was whatever DI/provider
/// discovery order happened to produce — not a deliberate choice. Pins that every provider now
/// declares one, and that they agree with each other (a provider silently left off any future
/// addition would otherwise fall back to Jellyfin's generic default and reintroduce the gap).
/// </summary>
public class ProviderOrderTests
{
    [Fact]
    public void All_metadata_and_image_providers_declare_an_explicit_order()
    {
        IHasOrder[] providers =
        {
            new MetaHubMovieProvider(null!, null!, null!, null!),
            new MetaHubSeriesProvider(null!, null!, null!, null!),
            new MetaHubSeasonProvider(null!, null!, null!),
            new MetaHubEpisodeProvider(null!, null!, null!),
            new MetaHubImageProvider(null!, null!, null!),
        };

        Assert.All(providers, p => Assert.Equal(40, p.Order));
    }
}
