namespace MetaHub.Domain.Enums;

/// <summary>
/// How a local <see cref="Entities.MediaFile"/> was matched to a work/episode.
/// Mirrors the Shoko principle: exact identification over filename guessing.
/// </summary>
public enum IdentificationMethod
{
    /// <summary>Not yet identified.</summary>
    None = 0,
    /// <summary>ED2K (optionally CRC32) file hash resolved via AniDB. Anime.</summary>
    Ed2k = 1,
    /// <summary>Chromaprint acoustic fingerprint resolved via AcoustID. Music.</summary>
    AcoustId = 2,
    /// <summary>ISBN read from file/folder metadata. Books.</summary>
    Isbn = 3,
    /// <summary>OpenSubtitles size+chunk movie hash. Optional film/series signal.</summary>
    MovieHash = 4,
    /// <summary>Cleaned filename parsed and resolved via TMDB/TVDB. Film/series fallback.</summary>
    Filename = 5,
    /// <summary>Manually assigned by a user.</summary>
    Manual = 6
}
