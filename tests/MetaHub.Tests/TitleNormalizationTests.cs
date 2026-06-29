using MetaHub.Domain;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Unit tests for the shared title normalization. The separator set must strip brackets and
/// parentheses too: manami/AniList canonical titles wrap a part name in square brackets
/// ("Fate/stay night [Heaven's Feel] I. presage flower") while libraries are usually filed with
/// round parentheses or none ("Fate/stay night (Heaven's Feel) I. presage flower"). If brackets
/// are not removed the two never compare equal and the work resolves to nothing (title-only
/// last-resort metadata, no ids/overview/artwork/people).
/// </summary>
public class TitleNormalizationTests
{
    [Theory]
    [InlineData("Fate/stay night [Heaven's Feel] I. presage flower", "fatestaynightheavensfeelipresageflower")]
    [InlineData("Fate/stay night (Heaven's Feel) I. presage flower", "fatestaynightheavensfeelipresageflower")]
    [InlineData("Fate stay night Heavens Feel I presage flower", "fatestaynightheavensfeelipresageflower")]
    [InlineData("Ane Log: Moyako {Nee-san}", "anelogmoyakoneesan")]
    public void Normalize_strips_brackets_and_parentheses(string input, string expected)
        => Assert.Equal(expected, TitleNormalization.Normalize(input));

    [Fact]
    public void Bracketed_and_parenthesised_forms_normalize_equal()
    {
        var bracket = TitleNormalization.Normalize("Fate/stay night [Heaven's Feel] II. lost butterfly");
        var paren = TitleNormalization.Normalize("Fate/stay night (Heaven's Feel) II. lost butterfly");
        var bare = TitleNormalization.Normalize("Fate stay night Heavens Feel II lost butterfly");
        Assert.Equal(bracket, paren);
        Assert.Equal(bracket, bare);
    }

    [Fact]
    public void SearchTitles_match_a_bracketed_title_from_a_parenthesised_needle()
    {
        // The stored search blob is built from the bracketed canonical; a folder using parentheses
        // must still produce a contained needle.
        var blob = TitleNormalization.BuildSearchTitles(new[] { "Fate/stay night [Heaven's Feel] III. spring song" });
        var needle = TitleNormalization.SearchNeedle("Fate/stay night (Heaven's Feel) III. spring song");
        Assert.Contains(needle, blob);
    }
}
