using MetaHub.Domain.Enums;

namespace MetaHub.Api.Contracts;

/// <summary>Media-type-neutral canonical view of a work plus its known cross-IDs.</summary>
public record WorkResponse(
    Guid Id,
    MediaType MediaType,
    string CanonicalTitle,
    string? OriginalTitle,
    int? ReleaseYear,
    string? Overview,
    WorkStatus Status,
    IReadOnlyList<ExternalIdResponse> ExternalIds);

public record ExternalIdResponse(string Source, string Value);

public record ImageResponse(string Type, string Url, string? Lang, int? Width, int? Height, string? Source, double Score);

public record EpisodeResponse(
    Guid Id,
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteNumber,
    DateOnly? AirDate,
    string? Title,
    string? Overview);

/// <summary>Result of identifying a local file against a work/episode.</summary>
public record IdentifyResponse(
    bool Identified,
    Guid? WorkId,
    Guid? EpisodeId,
    string IdentifiedBy,
    double Confidence,
    string? Note);

/// <summary>Request body for <c>POST /api/identify</c>.</summary>
public record IdentifyRequest(
    string? Path,
    string? Ed2kHash,
    string? AcoustId,
    string? Isbn,
    string? MovieHash);
