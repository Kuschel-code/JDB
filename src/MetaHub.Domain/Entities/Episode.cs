namespace MetaHub.Domain.Entities;

/// <summary>
/// A single episode of a series/anime. <see cref="AbsoluteNumber"/> captures the
/// anime-specific absolute numbering used by AniDB.
/// </summary>
public class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning series work (FK to <see cref="SeriesDetail.WorkId"/>).</summary>
    public Guid SeriesWorkId { get; set; }
    public SeriesDetail? Series { get; set; }

    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }

    /// <summary>Absolute episode number across all seasons (important for anime).</summary>
    public int? AbsoluteNumber { get; set; }

    public DateOnly? AirDate { get; set; }

    public string? Title { get; set; }
    public string? Overview { get; set; }

    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
}
