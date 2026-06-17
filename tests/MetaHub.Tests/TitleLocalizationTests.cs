using System.Collections.Generic;
using MetaHub.Jellyfin.Api;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Title selection at serve time: MetaHub must show a name in the viewer's language rather than
/// the romaji canonical title an anime carries. Mirrors how the overview is already localized.
/// </summary>
public class TitleLocalizationTests
{
    private const string Romaji = "Youkoso Jitsuryoku Shijou Shugi no Kyoushitsu e";

    private static Dictionary<string, string> Titles() => new()
    {
        ["en"] = "Classroom of the Elite",
        ["ja"] = "ようこそ実力至上主義の教室へ"
    };

    [Fact]
    public void Prefers_english_over_romaji_when_no_title_in_the_preferred_language()
    {
        // German viewer, no German title exists -> fall back to English, never the romaji canonical.
        Assert.Equal("Classroom of the Elite", MetaHubBackend.PickTitle(Romaji, Titles(), "de"));
    }

    [Fact]
    public void Prefers_the_exact_preferred_language_title_when_present()
    {
        Assert.Equal("ようこそ実力至上主義の教室へ", MetaHubBackend.PickTitle(Romaji, Titles(), "ja"));
    }

    [Fact]
    public void Falls_back_to_canonical_when_no_translation_is_available()
    {
        Assert.Equal(Romaji, MetaHubBackend.PickTitle(Romaji, new Dictionary<string, string>(), "de"));
    }

    [Fact]
    public void Ignores_blank_translations_and_uses_canonical()
    {
        var titles = new Dictionary<string, string> { ["en"] = "  " };
        Assert.Equal(Romaji, MetaHubBackend.PickTitle(Romaji, titles, "de"));
    }
}
