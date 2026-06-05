using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// Per-source rate-limit bookkeeping so enrichment workers can stay within
/// provider quotas (e.g. MusicBrainz/AniDB).
/// </summary>
public class SourceFetchLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ExternalIdSource Source { get; set; }

    public DateTimeOffset LastCallAt { get; set; }

    /// <summary>Number of calls made in the current rate-limit window.</summary>
    public int CallsInWindow { get; set; }
}
