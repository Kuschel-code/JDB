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
    IReadOnlyList<ExternalIdResponse> ExternalIds,
    IReadOnlyList<PersonResponse> People);

public record ExternalIdResponse(string Source, string Value);

/// <summary>A cast/crew member (actor, voice actor, director, author, ...).</summary>
public record PersonResponse(string Name, string Role, string? Character, string? ImageUrl, int Order);

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

/// <summary>Request body for <c>POST /api/files/identify</c> (ED2K + AniDB pipeline).</summary>
public record IdentifyFileRequest(string Path, bool ForceRehash = false);

/// <summary>Result of the server-side file identification pipeline (M3).</summary>
public record FileIdentifyResponse(
    Guid MediaFileId,
    bool Identified,
    Guid? WorkId,
    Guid? EpisodeId,
    string Method,
    double Confidence,
    string? Ed2kHash,
    string? Note);
