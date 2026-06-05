namespace MetaHub.Enrichment;

/// <summary>
/// Computes a comparable quality score for an image so the "best" one can be picked per type
/// (M8). Combines the provider's base quality, resolution, and language preference.
/// </summary>
public static class ImageScorer
{
    public static double Score(NormalizedImage image, string? preferredLanguage)
    {
        var score = image.Score; // provider base quality

        if (image.Width is { } w && image.Height is { } h && w > 0 && h > 0)
            score += Math.Min(20.0, w * (double)h / 100_000.0);

        if (!string.IsNullOrWhiteSpace(image.Lang))
        {
            if (preferredLanguage is not null &&
                image.Lang.Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase))
                score += 15.0; // matches the user's preferred language
        }
        else
        {
            score += 5.0; // language-neutral artwork is broadly usable
        }

        return score;
    }
}
