using System;
using MediaBrowser.Model.Entities;
using MetaHub.Jellyfin;
using MetaHub.Jellyfin.Api;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// The "apply best metadata to the library" task overwrites an item field only when MetaHub has a
/// value, it differs from the current one, and the field is not locked. work.CanonicalTitle is
/// already localized by the backend, so the task just compares against it.
/// </summary>
public class MetaHubItemApplyTests
{
    private static WorkDto Work(string title, string? overview = "An English overview.", int? year = 2018) =>
        new() { CanonicalTitle = title, Overview = overview, ReleaseYear = year, MediaType = "Anime" };

    private static readonly MetadataField[] None = Array.Empty<MetadataField>();

    [Fact]
    public void Replaces_a_romaji_title_with_metahubs_localized_one()
    {
        var current = new MetaHubItemApply.ItemFields("Sora yori mo Tooi Basho", "An English overview.", 2018);
        var changes = MetaHubItemApply.Compute(current, Work("A Place Further Than the Universe"), None);

        Assert.Equal("A Place Further Than the Universe", changes.Name);
        Assert.True(changes.HasAny);
    }

    [Fact]
    public void Makes_no_change_when_the_title_is_already_the_best()
    {
        var current = new MetaHubItemApply.ItemFields("A Place Further Than the Universe", "An English overview.", 2018);
        var changes = MetaHubItemApply.Compute(current, Work("A Place Further Than the Universe"), None);

        Assert.Null(changes.Name);
        Assert.False(changes.HasAny);
    }

    [Fact]
    public void Never_touches_a_locked_name()
    {
        var current = new MetaHubItemApply.ItemFields("Sora yori mo Tooi Basho", "An English overview.", 2018);
        var changes = MetaHubItemApply.Compute(current, Work("A Place Further Than the Universe"),
            new[] { MetadataField.Name });

        Assert.Null(changes.Name);
        Assert.False(changes.HasAny);
    }

    [Fact]
    public void Fills_a_missing_overview()
    {
        var current = new MetaHubItemApply.ItemFields("A Place Further Than the Universe", null, 2018);
        var changes = MetaHubItemApply.Compute(current, Work("A Place Further Than the Universe"), None);

        Assert.Equal("An English overview.", changes.Overview);
    }

    [Fact]
    public void Never_touches_a_locked_overview()
    {
        var current = new MetaHubItemApply.ItemFields("A Place Further Than the Universe", null, 2018);
        var changes = MetaHubItemApply.Compute(current, Work("A Place Further Than the Universe"),
            new[] { MetadataField.Overview });

        Assert.Null(changes.Overview);
    }

    [Fact]
    public void Does_not_blank_a_field_when_metahub_has_no_value()
    {
        var current = new MetaHubItemApply.ItemFields("A Place Further Than the Universe", "Keep me.", 2018);
        var changes = MetaHubItemApply.Compute(current, Work("A Place Further Than the Universe", overview: null), None);

        Assert.Null(changes.Overview);
        Assert.False(changes.HasAny);
    }
}
