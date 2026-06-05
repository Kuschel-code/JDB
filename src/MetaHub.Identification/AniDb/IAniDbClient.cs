namespace MetaHub.Identification.AniDb;

/// <summary>Looks up the authoritative AniDB record for a local file by size + ED2K hash.</summary>
public interface IAniDbClient
{
    /// <summary>True when AniDB integration is configured and enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Returns the AniDB file record, or null if disabled or not found.</summary>
    Task<AniDbFileResult?> LookupFileAsync(long sizeBytes, string ed2kHash, CancellationToken ct = default);
}
