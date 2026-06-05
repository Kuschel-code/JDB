using MetaHub.Domain.Enums;
using MetaHub.Ingest.Anime;
using Xunit;

namespace MetaHub.Tests;

public class ManamiSourceParserTests
{
    [Theory]
    [InlineData("https://anidb.net/anime/1", ExternalIdSource.AniDb, "1")]
    [InlineData("https://anilist.co/anime/263", ExternalIdSource.AniList, "263")]
    [InlineData("https://myanimelist.net/anime/1535", ExternalIdSource.Mal, "1535")]
    [InlineData("https://kitsu.app/anime/1376", ExternalIdSource.Kitsu, "1376")]
    [InlineData("https://anime-planet.com/anime/cowboy-bebop", ExternalIdSource.AnimePlanet, "cowboy-bebop")]
    [InlineData("https://notify.moe/anime/abc123", ExternalIdSource.Notify, "abc123")]
    [InlineData("https://livechart.me/anime/3556", ExternalIdSource.LiveChart, "3556")]
    public void Parses_known_sources(string url, ExternalIdSource expectedSource, string expectedValue)
    {
        var ok = ManamiSourceParser.TryParse(url, out var source, out var value);

        Assert.True(ok);
        Assert.Equal(expectedSource, source);
        Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData("https://example.com/anime/1")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Rejects_unknown_or_invalid(string url)
    {
        Assert.False(ManamiSourceParser.TryParse(url, out _, out _));
    }
}
