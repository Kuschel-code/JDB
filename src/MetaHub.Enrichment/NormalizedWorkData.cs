using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment;

/// <summary>
/// Provider-neutral, normalized metadata for a work. Every metadata provider parses its raw
/// response into this shape; <see cref="WorkMerger"/> then merges several of them into a
/// canonical <see cref="Domain.Entities.Work"/> using per-field source priority.
///
/// Optional detail fields cover all media types so the same pipeline serves anime, movies,
/// series, music and books.
/// </summary>
public class NormalizedWorkData
{
    public ExternalIdSource Source { get; init; }

    // Common
    public string? CanonicalTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Overview { get; set; }
    public Dictionary<string, string> OverviewTranslations { get; } = new();
    /// <summary>Locale-keyed title translations, e.g. {"en": "Classroom of the Elite", "ja": "…"}.</summary>
    public Dictionary<string, string> TitleTranslations { get; } = new();
    public WorkStatus? Status { get; set; }
    public List<string> Genres { get; } = new();
    public List<NormalizedImage> Images { get; } = new();
    public List<NormalizedCredit> Credits { get; } = new();

    // Series / anime
    public int? SeasonCount { get; set; }
    public int? EpisodeCount { get; set; }
    public string? Network { get; set; }

    // Music
    public string? AlbumType { get; set; }
    public string? Label { get; set; }
    public int? TrackCount { get; set; }

    // Book
    public string? Isbn13 { get; set; }
    public int? PageCount { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }
    public double? SeriesIndex { get; set; }
}

/// <summary>A cast or crew credit as reported by a provider (actor, director, author, …).</summary>
public class NormalizedCredit
{
    public required string Name { get; init; }
    public required CreditRole Role { get; init; }
    /// <summary>The character portrayed, for acting/voice roles.</summary>
    public string? Character { get; init; }
    public string? ImageUrl { get; init; }
    public int Order { get; init; }
}

public class NormalizedImage
{
    public required ImageType Type { get; init; }
    public required string Url { get; init; }
    public string? Lang { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Source { get; init; }
    public double Score { get; init; }
}
