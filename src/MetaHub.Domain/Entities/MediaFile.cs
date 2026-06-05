using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// A concrete local file in the user's library and the identifiers used to match it
/// exactly to a <see cref="Work"/> (and, for series/anime, an <see cref="Episode"/>).
/// This is the heart of the Shoko-style "identify, don't guess" approach.
/// </summary>
public class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null until the file has been identified.</summary>
    public Guid? WorkId { get; set; }
    public Work? Work { get; set; }

    /// <summary>Set for series/anime files once the specific episode is known.</summary>
    public Guid? EpisodeId { get; set; }
    public Episode? Episode { get; set; }

    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    // Anime (AniDB)
    public string? Ed2kHash { get; set; }
    public string? Crc32 { get; set; }

    // Music (AcoustID / MusicBrainz)
    public string? AcoustId { get; set; }
    public string? MbRecording { get; set; }

    // Optional film/series (OpenSubtitles)
    public string? MovieHash { get; set; }

    public IdentificationMethod IdentifiedBy { get; set; } = IdentificationMethod.None;
    public DateTimeOffset? IdentifiedAt { get; set; }

    /// <summary>Confidence of the identification in [0, 1].</summary>
    public double Confidence { get; set; }
}
