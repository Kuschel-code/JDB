using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment;

/// <summary>
/// A single external metadata source (AniList, Jikan, TMDB, MusicBrainz, Open Library, ...).
/// Splitting "fetch raw" from "parse" keeps parsing unit-testable and lets the enrichment
/// service handle caching/TTL uniformly.
/// </summary>
public interface IMetadataProvider
{
    ExternalIdSource Source { get; }

    /// <summary>
    /// Lower runs first and therefore wins per-field conflicts (it is more authoritative for
    /// this provider's domain).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// A floor under the enrichment cache lifetime for this provider, for sources whose terms
    /// are stricter than the global TTL (AniDB allows one fetch per anime per day and bans
    /// aggressively). Null — the default — means the global TTL applies unchanged.
    /// </summary>
    TimeSpan? MinCacheTtl => null;

    /// <summary>Returns the provider's external id for this work, or null if not applicable.</summary>
    string? GetExternalId(Work work);

    /// <summary>
    /// Fetches the raw response body for the given id (HTTP, Polly-protected). Null on miss.
    /// The <paramref name="work"/> is supplied for context (e.g. TMDB movie vs. tv endpoint).
    /// </summary>
    Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default);

    /// <summary>Parses a raw response body into normalized data.</summary>
    NormalizedWorkData Parse(string rawBody);
}
