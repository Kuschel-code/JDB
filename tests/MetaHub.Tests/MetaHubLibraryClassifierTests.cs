using MetaHub.Domain.Enums;
using MetaHub.Jellyfin;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the library-name → media-type heuristic and override parsing that let MetaHub bias
/// title matching by the library an item lives in (e.g. anime "22/7" over US series "227").
/// </summary>
public class MetaHubLibraryClassifierTests
{
    [Theory]
    [InlineData("Anime", MediaType.Anime)]
    [InlineData("My Anime", MediaType.Anime)]
    [InlineData("Serien", MediaType.Series)]
    [InlineData("Series", MediaType.Series)]
    [InlineData("TV Shows", MediaType.Series)]
    [InlineData("Filme", MediaType.Movie)]
    [InlineData("Movies", MediaType.Movie)]
    [InlineData("Kinofilme", MediaType.Movie)]
    [InlineData("Musik", MediaType.Music)]
    [InlineData("Music", MediaType.Music)]
    [InlineData("Bücher", MediaType.Book)]
    [InlineData("Books", MediaType.Book)]
    [InlineData("Manga", MediaType.Book)]
    public void FromLibraryName_maps_known_names(string name, MediaType expected)
        => Assert.Equal(expected, MetaHubLibraryClassifier.FromLibraryName(name));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Home Videos")]
    [InlineData("Collections")]
    public void FromLibraryName_unknown_is_null(string? name)
        => Assert.Null(MetaHubLibraryClassifier.FromLibraryName(name));

    [Fact]
    public void Anime_wins_over_series_when_both_appear()
        => Assert.Equal(MediaType.Anime, MetaHubLibraryClassifier.FromLibraryName("Anime Serien"));

    [Theory]
    [InlineData("Anime", MediaType.Anime)]
    [InlineData("series", MediaType.Series)]
    [InlineData("film", MediaType.Movie)]
    [InlineData("musik", MediaType.Music)]
    [InlineData("buch", MediaType.Book)]
    public void ParseType_parses_known_values(string value, MediaType expected)
        => Assert.Equal(expected, MetaHubLibraryClassifier.ParseType(value));

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void ParseType_auto_or_unknown_is_null(string? value)
        => Assert.Null(MetaHubLibraryClassifier.ParseType(value));
}
