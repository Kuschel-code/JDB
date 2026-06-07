using MetaHub.Jellyfin;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the pure decision core of the gate (id + ancestor ids vs. the disabled set).
/// The ILibraryManager-backed resolution is a thin wrapper and is exercised live, not here.
/// </summary>
public class MetaHubItemGateTests
{
    [Fact]
    public void Empty_disabled_set_serves_everything()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Assert.False(MetaHubItemGate.IsBlocked(ids, Array.Empty<string>()));
    }

    [Fact]
    public void Blocks_when_item_itself_is_disabled()
    {
        var item = Guid.NewGuid();
        Assert.True(MetaHubItemGate.IsBlocked(new[] { item }, new[] { item.ToString("N") }));
    }

    [Fact]
    public void Blocks_when_an_ancestor_is_disabled()
    {
        var item = Guid.NewGuid();
        var series = Guid.NewGuid();
        var library = Guid.NewGuid();
        Assert.True(MetaHubItemGate.IsBlocked(
            new[] { item, series, library }, new[] { library.ToString("N") }));
    }

    [Fact]
    public void Serves_when_neither_item_nor_ancestor_is_disabled()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        Assert.False(MetaHubItemGate.IsBlocked(ids, new[] { Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public void Ignores_empty_guids_and_is_case_insensitive()
    {
        var item = Guid.NewGuid();
        Assert.False(MetaHubItemGate.IsBlocked(new[] { Guid.Empty }, new[] { Guid.Empty.ToString("N") }));
        Assert.True(MetaHubItemGate.IsBlocked(new[] { item }, new[] { item.ToString("N").ToUpperInvariant() }));
    }
}
