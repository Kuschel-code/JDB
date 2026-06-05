using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// The canonical, media-type-neutral representation of a single work
/// (an album, movie, series, anime or book). This is MetaHub's internal
/// "source of truth" that external provider data is merged into.
/// </summary>
public class Work
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public MediaType MediaType { get; set; }

    public string CanonicalTitle { get; set; } = string.Empty;

    public string? OriginalTitle { get; set; }

    public int? ReleaseYear { get; set; }

    /// <summary>Default overview text (typically EN). Translations live in <see cref="OverviewTranslations"/>.</summary>
    public string? Overview { get; set; }

    /// <summary>Locale-keyed overview translations, e.g. {"de": "...", "ja": "..."}. Stored as JSONB.</summary>
    public Dictionary<string, string> OverviewTranslations { get; set; } = new();

    public WorkStatus Status { get; set; } = WorkStatus.Unknown;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<ExternalId> ExternalIds { get; set; } = new List<ExternalId>();
    public ICollection<Image> Images { get; set; } = new List<Image>();
    public ICollection<Credit> Credits { get; set; } = new List<Credit>();
    public ICollection<WorkGenre> WorkGenres { get; set; } = new List<WorkGenre>();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public ICollection<RawPayload> RawPayloads { get; set; } = new List<RawPayload>();

    // Optional one-to-one detail blocks (only one is populated per media type)
    public SeriesDetail? SeriesDetail { get; set; }
    public MusicDetail? MusicDetail { get; set; }
    public BookDetail? BookDetail { get; set; }
}
