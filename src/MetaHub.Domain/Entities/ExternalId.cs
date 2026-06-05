using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// A cross-reference linking a <see cref="Work"/> to an identifier in an external provider.
/// The combination of (Source, ExternalValue) is unique across the system.
/// </summary>
public class ExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public ExternalIdSource Source { get; set; }

    /// <summary>The identifier value as the provider expresses it (e.g. a TMDB id, an MBID, an ISBN-13).</summary>
    public string ExternalValue { get; set; } = string.Empty;
}
