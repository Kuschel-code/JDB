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

    /// <summary>Returns the artwork for a resolved work.</summary>
    Task<IReadOnlyList<ImageDto>> GetImagesAsync(Guid workId, CancellationToken ct);
}
