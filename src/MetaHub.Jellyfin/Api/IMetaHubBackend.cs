using MetaHub.Domain.Enums;

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
    /// order against canonical and original titles (case-insensitive, punctuation-insensitive so
    /// "227" matches "22/7"); a year, when given, disambiguates multiple matches. When
    /// <paramref name="preferredType"/> is set (from the item's library), only works of that
    /// media type are considered. <paramref name="folderName"/> (the item's folder) is the
    /// authority for the season/sequel distinction, so "… Reawakened" never resolves to the base
    /// series. Returns null when no unambiguous match exists.
    /// </summary>
    Task<WorkDto?> ResolveByNameAsync(
        IEnumerable<string> nameCandidates, int? year, MediaType? preferredType,
        string? folderName, string? lang, CancellationToken ct);

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
