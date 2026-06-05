using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// Artwork associated with a work. Multiple candidates can exist per type and
/// are ranked by <see cref="Score"/> during conflict resolution.
/// </summary>
public class Image
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public ImageType Type { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>BCP-47 / ISO language tag of any embedded text, or null if language-neutral.</summary>
    public string? Lang { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Provider that supplied this image (e.g. "tmdb", "fanarttv").</summary>
    public string? Source { get; set; }

    /// <summary>Computed quality score used to pick the "best" image (resolution, language, votes).</summary>
    public double Score { get; set; }
}
