using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using Xunit;

namespace MetaHub.Tests;

public class ImageScorerTests
{
    private static NormalizedImage Img(double baseScore, int? w = null, int? h = null, string? lang = null)
        => new() { Type = ImageType.Poster, Url = "u", Score = baseScore, Width = w, Height = h, Lang = lang };

    [Fact]
    public void Higher_resolution_scores_higher()
    {
        var low = ImageScorer.Score(Img(50, 100, 100), preferredLanguage: null);
        var high = ImageScorer.Score(Img(50, 2000, 3000), preferredLanguage: null);
        Assert.True(high > low);
    }

    [Fact]
    public void Preferred_language_match_boosts_score()
    {
        var match = ImageScorer.Score(Img(50, lang: "de"), preferredLanguage: "de");
        var other = ImageScorer.Score(Img(50, lang: "ja"), preferredLanguage: "de");
        Assert.True(match > other);
    }

    [Fact]
    public void Resolution_bonus_is_capped()
    {
        // Non-null, non-preferred language => no language bonus, isolating the resolution cap.
        var huge = ImageScorer.Score(Img(0, 100_000, 100_000, lang: "ja"), preferredLanguage: "de");
        Assert.True(huge <= 20.0 + 0.0001);
    }
}
