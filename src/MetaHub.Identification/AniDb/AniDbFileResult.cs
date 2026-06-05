namespace MetaHub.Identification.AniDb;

/// <summary>
/// The subset of AniDB FILE data MetaHub needs to link a local file to an anime/episode.
/// </summary>
public class AniDbFileResult
{
    public required string FileId { get; init; }
    public required string AnimeId { get; init; }
    public required string EpisodeId { get; init; }
    public string? GroupId { get; init; }

    /// <summary>Episode number string (may be like "12", "S1", "OVA2").</summary>
    public string? EpisodeNumber { get; init; }

    public string? AnimeType { get; init; }
    public string? AnimeTitleEnglish { get; init; }

    /// <summary>The raw, pipe-delimited AniDB response line (stored for re-processing).</summary>
    public required string RawResponse { get; init; }
}
