using MetaHub.Identification.Naming;
using Xunit;

namespace MetaHub.Tests;

public class FilenameParserTests
{
    [Theory]
    [InlineData("Cowboy.Bebop.S01E05.1080p.BluRay.mkv", "Cowboy Bebop", 1, 5)]
    [InlineData("The Wire - s03e09 - title.mkv", "The Wire", 3, 9)]
    [InlineData("Show_Name_S1E12.mp4", "Show Name", 1, 12)]
    public void Parses_series_episode(string file, string title, int season, int episode)
    {
        var parsed = FilenameParser.Parse(file);
        Assert.True(parsed.IsEpisode);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(season, parsed.Season);
        Assert.Equal(episode, parsed.Episode);
    }

    [Theory]
    [InlineData("Akira (1988).mkv", "Akira", 1988)]
    [InlineData("Blade.Runner.1982.2160p.mkv", "Blade Runner", 1982)]
    public void Parses_movie_with_year(string file, string title, int year)
    {
        var parsed = FilenameParser.Parse(file);
        Assert.False(parsed.IsEpisode);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(year, parsed.Year);
    }
}
