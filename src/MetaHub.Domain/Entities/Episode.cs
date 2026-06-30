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

    /// <summary>AniDB episode id (eid) from the HTTP anime XML.</summary>
    public int? AniDbEpisodeId { get; set; }

    /// <summary>AniDB episode type: Regular, Special, Credit, Trailer, Parody, Other.</summary>
    public EpisodeKind Kind { get; set; } = EpisodeKind.Regular;

    /// <summary>Raw AniDB episode number string (e.g. "12", "S1", "C3", "T2").</summary>
    public string? RawEpno { get; set; }

    public DateOnly? AirDate { get; set; }

    public string? Title { get; set; }
    public string? Overview { get; set; }

    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
}

public enum EpisodeKind
{
    Regular = 1,
    Special = 2,
    Credit = 3,
    Trailer = 4,
    Parody = 5,
    Other = 6
}
