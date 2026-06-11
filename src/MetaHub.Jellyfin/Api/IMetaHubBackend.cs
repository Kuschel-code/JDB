namespace MetaHub.Jellyfin.Api;

/// <summary>
/// Abstracts where the plugin gets its data from: the in-process embedded engine (local
/// SQLite) or a remote MetaHub API. Providers depend on this, not on a transport.
/// </summary>
public interface IMetaHubBackend
{
    /// <summary>Resolves a work from a Jellyfin item's provider ids (with optional language).</summary>
    Task<WorkDto?> ResolveAsync(
        IReadOnlyDictionary<string, string> providerIds, string? lang, CancellationToken ct);

    /// <summary>
    /// Resolves a work by title when no provider id matched. Tries each candidate name in
    /// order against canonical and original titles (case-insensitive); a year, when given,
    /// disambiguates multiple matches. Returns null when no unambiguous match exists.
    /// </summary>
    Task<WorkDto?> ResolveByNameAsync(
        IEnumerable<string> nameCandidates, int? year, string? lang, CancellationToken ct);

    /// <summary>Returns the artwork for a resolved work.</summary>
    Task<IReadOnlyList<ImageDto>> GetImagesAsync(Guid workId, CancellationToken ct);

    /// <summary>
    /// Resolves a single episode of the series identified by <paramref name="seriesProviderIds"/>.
    /// Matches season+episode first; for absolutely-numbered libraries (common for anime) a
    /// season-1/absolute-number match is used as fallback. Null when unknown.
    /// </summary>
    Task<EpisodeDto?> GetEpisodeAsync(
        IReadOnlyDictionary<string, string> seriesProviderIds, int? seasonNumber, int? episodeNumber,
        CancellationToken ct);
}

public class EpisodeDto
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteNumber { get; set; }
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public DateOnly? AirDate { get; set; }

    /// <summary>Media type of the owning series (for the media-type gate).</summary>
    public string SeriesMediaType { get; set; } = string.Empty;
}
