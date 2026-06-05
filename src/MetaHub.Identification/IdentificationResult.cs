using MetaHub.Domain.Enums;

namespace MetaHub.Identification;

/// <summary>Outcome of identifying a single local file.</summary>
public class IdentificationResult
{
    public required Guid MediaFileId { get; init; }
    public bool Identified { get; init; }
    public Guid? WorkId { get; init; }
    public Guid? EpisodeId { get; init; }
    public IdentificationMethod Method { get; init; }
    public double Confidence { get; init; }
    public string? Ed2kHash { get; init; }
    public string? Note { get; init; }
}
