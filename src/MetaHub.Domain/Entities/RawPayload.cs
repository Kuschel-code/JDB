using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// The unmodified response body from an external provider, stored as JSONB so the
/// normalization step can be changed and re-run later without re-hitting the API.
/// </summary>
public class RawPayload
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public ExternalIdSource Source { get; set; }

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

    public int HttpStatus { get; set; }

    /// <summary>Raw response body (JSONB).</summary>
    public string Body { get; set; } = "{}";
}
