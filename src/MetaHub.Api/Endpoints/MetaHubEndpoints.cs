using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MetaHub.Api.Contracts;
using MetaHub.Api.Scheduling;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Export;
using MetaHub.Identification;
using MetaHub.Identification.AniDb;
using MetaHub.Infrastructure;
using MetaHub.Ingest.Anime;

namespace MetaHub.Api.Endpoints;

/// <summary>
/// The thin, stable "single source of truth" API for clients (Abschnitt 10 of the concept).
/// </summary>
public static class MetaHubEndpoints
{
    public static void MapMetaHubEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/work/{id:guid}", GetWork);
        api.MapGet("/work/{id:guid}/images", GetWorkImages);
        api.MapGet("/series/{id:guid}/episodes", GetSeriesEpisodes);
        api.MapGet("/lookup", Lookup);
        api.MapGet("/search", Search);
        api.MapGet("/config", GetConfig);
        api.MapPost("/identify", Identify);
        api.MapPost("/files/identify", IdentifyFile);
        api.MapGet("/work/{id:guid}/nfo", GetWorkNfo);

        // Admin / operational endpoints.
        var admin = app.MapGroup("/api/admin");
        admin.MapPost("/ingest/anime", RunAnimeIngest);
        admin.MapPost("/enrich/work/{id:guid}", EnrichWork);
        admin.MapPost("/enrich/anime", EnrichAnime);
        admin.MapPost("/export/nfo/{id:guid}", ExportNfo);
        admin.MapGet("/stats", GetStats);
    }

    private static async Task<IResult> GetWork(Guid id, MetaHubDbContext db, CancellationToken ct, string? lang = null)
    {
        var work = await db.Works
            .Include(w => w.ExternalIds)
            .Include(w => w.Credits).ThenInclude(c => c.Person)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        return work is null ? Results.NotFound() : Results.Ok(ToResponse(work, lang));
    }

    private static async Task<IResult> GetWorkImages(Guid id, string? type, MetaHubDbContext db, CancellationToken ct)
    {
        var query = db.Images.AsNoTracking().Where(i => i.WorkId == id);
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ImageType>(type, true, out var imageType))
            query = query.Where(i => i.Type == imageType);

        var images = await query
            .OrderByDescending(i => i.Score)
            .Select(i => new ImageResponse(
                i.Type.ToString(), i.Url, i.Lang, i.Width, i.Height, i.Source, i.Score))
            .ToListAsync(ct);

        return Results.Ok(images);
    }

    private static async Task<IResult> GetSeriesEpisodes(Guid id, MetaHubDbContext db, CancellationToken ct)
    {
        var episodes = await db.Episodes.AsNoTracking()
            .Where(e => e.SeriesWorkId == id)
            .OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => new EpisodeResponse(
                e.Id, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteNumber, e.AirDate, e.Title, e.Overview))
            .ToListAsync(ct);

        return Results.Ok(episodes);
    }

    private static async Task<IResult> Lookup(
        string source, string id, MetaHubDbContext db, CancellationToken ct, string? lang = null)
    {
        if (!TryParseSource(source, out var src))
            return Results.BadRequest($"Unknown source '{source}'.");

        // A bare "tmdb" lookup must also match movie ids stored under TmdbMovie (TMDB's tv and movie
        // id spaces overlap), mirroring the embedded FindWorkIdAsync.
        var alsoTmdbMovie = src == ExternalIdSource.Tmdb;
        var work = await db.Works
            .Include(w => w.ExternalIds)
            .Include(w => w.Credits).ThenInclude(c => c.Person)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.ExternalIds.Any(x => x.ExternalValue == id
                    && (x.Source == src || (alsoTmdbMovie && x.Source == ExternalIdSource.TmdbMovie))), ct);

        return work is null ? Results.NotFound() : Results.Ok(ToResponse(work, lang));
    }

    private static async Task<IResult> Search(
        string? type, string q, MetaHubDbContext db, CancellationToken ct, int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.BadRequest("Query 'q' is required.");

        var query = db.Works.Include(w => w.ExternalIds).AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<MediaType>(type, true, out var mediaType))
            query = query.Where(w => w.MediaType == mediaType);

        var pattern = $"%{q}%";
        var works = await query
            .Where(w => EF.Functions.ILike(w.CanonicalTitle, pattern)
                     || (w.OriginalTitle != null && EF.Functions.ILike(w.OriginalTitle, pattern)))
            .OrderBy(w => w.CanonicalTitle)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Results.Ok(works.Select(w => ToResponse(w)));
    }

    private static async Task<IResult> Identify(
        IdentifyRequest request, MetaHubDbContext db, CancellationToken ct)
    {
        // M3 (ED2K hashing + AniDB lookup, AcoustID, ISBN) is not built yet. For now this
        // resolves against files that have already been identified and stored.
        MediaFile? file = null;
        if (!string.IsNullOrWhiteSpace(request.Ed2kHash))
            file = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Ed2kHash == request.Ed2kHash, ct);
        else if (!string.IsNullOrWhiteSpace(request.AcoustId))
            file = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(f => f.AcoustId == request.AcoustId, ct);
        else if (!string.IsNullOrWhiteSpace(request.Path))
            file = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Path == request.Path, ct);

        if (file is { WorkId: not null })
        {
            return Results.Ok(new IdentifyResponse(
                true, file.WorkId, file.EpisodeId, file.IdentifiedBy.ToString(), file.Confidence, null));
        }

        return Results.Ok(new IdentifyResponse(
            false, null, null, IdentificationMethod.None.ToString(), 0,
            "Not identified. Online identification (M3: ED2K/AniDB, AcoustID, ISBN) is not implemented yet."));
    }

    private static async Task<IResult> IdentifyFile(
        IdentifyFileRequest request, FileIdentificationService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return Results.BadRequest("'path' is required.");

        try
        {
            var r = await service.IdentifyAnimeFileAsync(request.Path, request.ForceRehash, ct);
            return Results.Ok(new FileIdentifyResponse(
                r.MediaFileId, r.Identified, r.WorkId, r.EpisodeId,
                r.Method.ToString(), r.Confidence, r.Ed2kHash, r.Note));
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound($"File not found: {request.Path}");
        }
    }

    private static async Task<IResult> RunAnimeIngest(AnimeIngestRunner runner, CancellationToken ct)
    {
        var (manami, fribb) = await runner.RunAsync(ct);
        return Results.Ok(new
        {
            manami = new { manami.Created, manami.Updated, manami.ExternalIdsAdded, manami.Skipped },
            fribb = new { fribb.Updated, fribb.ExternalIdsAdded, fribb.Skipped }
        });
    }

    private static async Task<IResult> EnrichWork(
        Guid id, bool? force, string? writeMode, EnrichmentService service, CancellationToken ct)
    {
        var mode = ParseWriteMode(writeMode);
        var result = await service.EnrichAsync(id, force ?? false, mode, ct);
        return result.Found
            ? Results.Ok(new { result.WorkId, result.Applied, result.Misses })
            : Results.NotFound();
    }

    private static async Task<IResult> EnrichAnime(
        EnrichmentRunner runner, CancellationToken ct,
        int max = 100, int delayMs = 1000, bool force = false, bool onlyMissing = false, string? writeMode = null)
    {
        var enriched = await runner.EnrichAnimeAsync(max, delayMs, force, onlyMissing, ParseWriteMode(writeMode), ct);
        return Results.Ok(new { enriched });
    }

    private static EnrichmentWriteMode? ParseWriteMode(string? value)
        => Enum.TryParse<EnrichmentWriteMode>(value, true, out var mode) ? mode : null;

    /// <summary>
    /// Read-only, sanitized view of the server's effective engine configuration. Secrets
    /// (passwords, API keys) are reported as booleans only. Used by the Jellyfin plugin's
    /// "Server" tab so operators can see how the engine is configured without SSH.
    /// </summary>
    private static IResult GetConfig(
        IOptions<EnrichmentOptions> enrichment,
        IOptions<AniDbOptions> aniDb,
        IOptions<SchedulerOptions> scheduler,
        IOptions<AnimeIngestOptions> ingest)
    {
        var e = enrichment.Value;
        var a = aniDb.Value;
        var s = scheduler.Value;
        var i = ingest.Value;

        return Results.Ok(new
        {
            enrichment = new
            {
                writeMode = e.WriteMode.ToString(),
                preferredLanguage = e.PreferredLanguage,
                e.TtlFinishedDays,
                e.TtlOngoingDays,
                tmdbConfigured = !string.IsNullOrWhiteSpace(e.TmdbApiKey),
                googleBooksConfigured = !string.IsNullOrWhiteSpace(e.GoogleBooksApiKey)
            },
            aniDb = new
            {
                a.Enabled,
                a.MinRequestIntervalSeconds,
                credentialsConfigured = !string.IsNullOrWhiteSpace(a.Username) && !string.IsNullOrWhiteSpace(a.Password)
            },
            scheduler = new
            {
                s.Enabled,
                s.EnrichmentEnabled,
                s.EnrichmentIntervalMinutes,
                s.EnrichmentOnlyMissing,
                s.IngestEnabled,
                s.IngestIntervalHours,
                s.ScanEnabled,
                s.ScanIntervalMinutes,
                animeLibraryPaths = s.AnimeLibraryPaths.Length
            },
            ingest = new { i.ManamiUrl, i.FribbUrl }
        });
    }

    private static async Task<IResult> GetStats(MetaHubDbContext db, CancellationToken ct)
    {
        var byType = await db.Works
            .GroupBy(w => w.MediaType)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            works = await db.Works.CountAsync(ct),
            worksByType = byType,
            externalIds = await db.ExternalIds.CountAsync(ct),
            mediaFiles = await db.MediaFiles.CountAsync(ct),
            identifiedFiles = await db.MediaFiles.CountAsync(f => f.WorkId != null, ct),
            images = await db.Images.CountAsync(ct),
            rawPayloads = await db.RawPayloads.CountAsync(ct)
        });
    }

    private static async Task<IResult> GetWorkNfo(Guid id, NfoExportService export, CancellationToken ct)
    {
        var doc = await export.BuildAsync(id, ct);
        return doc is null ? Results.NotFound() : Results.Text(doc.Content, "application/xml");
    }

    private static async Task<IResult> ExportNfo(
        Guid id, string dir, NfoExportService export, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return Results.BadRequest("'dir' query parameter is required.");

        var path = await export.WriteAsync(id, dir, ct);
        return path is null ? Results.NotFound() : Results.Ok(new { path });
    }

    private static WorkResponse ToResponse(Work w, string? lang = null)
    {
        // Prefer a localized overview when the requested language is available.
        var overview = w.Overview;
        if (!string.IsNullOrWhiteSpace(lang) &&
            w.OverviewTranslations.TryGetValue(lang, out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
            overview = localized;

        return new WorkResponse(
            w.Id, w.MediaType, w.CanonicalTitle, w.OriginalTitle, w.ReleaseYear, overview, w.Status,
            w.ExternalIds.Select(x => new ExternalIdResponse(x.Source.ToString(), x.ExternalValue)).ToList(),
            w.Credits
                .OrderBy(c => c.Order)
                .Where(c => c.Person is not null)
                .Select(c => new PersonResponse(
                    c.Person!.Name, c.Role.ToString(), c.Character, c.Person.ImageUrl, c.Order))
                .ToList());
    }

    private static bool TryParseSource(string source, out ExternalIdSource parsed)
    {
        // Accept the friendly aliases used in the concept's API examples.
        parsed = source.Trim().ToLowerInvariant() switch
        {
            "tmdb" => ExternalIdSource.Tmdb,
            "tvdb" => ExternalIdSource.Tvdb,
            "imdb" => ExternalIdSource.Imdb,
            "mbid" or "musicbrainz" => ExternalIdSource.MusicBrainz,
            "anilist" => ExternalIdSource.AniList,
            "mal" => ExternalIdSource.Mal,
            "anidb" => ExternalIdSource.AniDb,
            "kitsu" => ExternalIdSource.Kitsu,
            "isbn" => ExternalIdSource.Isbn,
            "openlibrary" => ExternalIdSource.OpenLibrary,
            "wikidata" => ExternalIdSource.Wikidata,
            _ => ExternalIdSource.Unknown
        };
        return parsed != ExternalIdSource.Unknown || Enum.TryParse(source, true, out parsed);
    }
}
