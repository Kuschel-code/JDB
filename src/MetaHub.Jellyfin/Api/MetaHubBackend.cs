using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Infrastructure;

namespace MetaHub.Jellyfin.Api;

/// <summary>
/// Backend that serves the plugin either from the embedded engine (local SQLite + in-process
/// enrichment) or from a remote MetaHub API, chosen per call from the plugin configuration.
/// A fresh DI scope is created per operation so this works regardless of provider lifetime.
/// </summary>
public class MetaHubBackend : IMetaHubBackend
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<Configuration.PluginConfiguration> _configAccessor;

    public MetaHubBackend(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        Func<Configuration.PluginConfiguration> configAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configAccessor = configAccessor;
    }

    private Configuration.PluginConfiguration Config => _configAccessor();

    public async Task<WorkDto?> ResolveAsync(
        IReadOnlyDictionary<string, string> providerIds, string? lang, CancellationToken ct)
    {
        if (!Config.UseEmbeddedEngine)
        {
            var client = new MetaHubApiClient(_httpClientFactory);
            return await MetaHubMapping.ResolveAsync(client, providerIds, lang, ct).ConfigureAwait(false);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        Work? work = null;
        if (await FindWorkIdAsync(db, providerIds, ct).ConfigureAwait(false) is { } wid)
            work = await LoadWorkAsync(db, wid, ct).ConfigureAwait(false);

        if (work is null)
            return null;

        // On-demand enrichment when the work has not been enriched yet (best-effort).
        if (Config.EnrichOnDemand && string.IsNullOrWhiteSpace(work.Overview))
        {
            try
            {
                var enrichment = scope.ServiceProvider.GetRequiredService<EnrichmentService>();
                await enrichment.EnrichAsync(work.Id, false, null, ct).ConfigureAwait(false);
                work = await LoadWorkAsync(db, work.Id, ct).ConfigureAwait(false) ?? work;
            }
            catch
            {
                // Enrichment is best-effort; serve whatever is already stored.
            }
        }

        return ToDto(work, lang);
    }

    public async Task<IReadOnlyList<ImageDto>> GetImagesAsync(Guid workId, CancellationToken ct)
    {
        if (!Config.UseEmbeddedEngine)
        {
            var client = new MetaHubApiClient(_httpClientFactory);
            return await client.GetImagesAsync(workId, ct).ConfigureAwait(false);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        return await db.Images
            .Where(i => i.WorkId == workId)
            .OrderByDescending(i => i.Score)
            .Select(i => new ImageDto
            {
                Type = i.Type.ToString(),
                Url = i.Url,
                Lang = i.Lang,
                Width = i.Width,
                Height = i.Height,
                Source = i.Source,
                Score = i.Score
            })
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<WorkDto?> ResolveByNameAsync(
        IEnumerable<string> nameCandidates, int? year, MediaType? preferredType, string? lang, CancellationToken ct)
    {
        var names = nameCandidates
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return null;

        if (!Config.UseEmbeddedEngine)
        {
            // Remote mode: title lookup is not exposed as a dedicated endpoint yet.
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        foreach (var name in names)
        {
            var lowered = name.ToLowerInvariant();
            var norm = NormTitle(name);

            // Restrict to the library's media type when known (e.g. only Anime in an "Anime" library).
            var query = db.Works.AsQueryable();
            if (preferredType is { } pt)
                query = query.Where(w => w.MediaType == pt);

            // Exact (case-insensitive) OR punctuation-insensitive match so a folder "227" finds
            // the anime "22/7". The Replace chain is translated to SQLite replace() by EF Core.
            var matches = await query
                .Where(w =>
                    w.CanonicalTitle.ToLower() == lowered
                    || (w.OriginalTitle != null && w.OriginalTitle.ToLower() == lowered)
                    || w.CanonicalTitle.ToLower()
                        .Replace(" ", "").Replace("/", "").Replace("-", "").Replace(":", "")
                        .Replace(".", "").Replace(",", "").Replace("'", "").Replace("!", "")
                        .Replace("?", "").Replace("_", "") == norm
                    || (w.OriginalTitle != null && w.OriginalTitle.ToLower()
                        .Replace(" ", "").Replace("/", "").Replace("-", "").Replace(":", "")
                        .Replace(".", "").Replace(",", "").Replace("'", "").Replace("!", "")
                        .Replace("?", "").Replace("_", "") == norm))
                .Select(w => new { w.Id, w.ReleaseYear, w.CanonicalTitle, w.OriginalTitle })
                .Take(8)
                .ToListAsync(ct).ConfigureAwait(false);

            if (matches.Count == 0)
                continue;

            // Prefer an exact title hit over a punctuation-only (fuzzy) hit.
            var exact = matches
                .Where(m => string.Equals(m.CanonicalTitle, name, StringComparison.OrdinalIgnoreCase)
                            || (m.OriginalTitle != null
                                && string.Equals(m.OriginalTitle, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var pool = exact.Count > 0 ? exact : matches;

            // Disambiguate by year when several works remain (remakes etc.).
            var pick = pool.Count == 1
                ? pool[0]
                : year is { } y
                    ? pool.FirstOrDefault(m => m.ReleaseYear == y)
                    : null;
            if (pick is null)
                continue;

            var work = await LoadWorkAsync(db, pick.Id, ct).ConfigureAwait(false);
            if (work is not null)
                return ToDto(work, lang);
        }

        return null;
    }

    /// <summary>Lowercase a title and strip common separators so "22/7" and "227" compare equal.
    /// Must mirror the SQL Replace chain in <see cref="ResolveByNameAsync"/>.</summary>
    private static string NormTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return string.Empty;
        var s = title.ToLowerInvariant();
        foreach (var sep in new[] { " ", "/", "-", ":", ".", ",", "'", "!", "?", "_" })
            s = s.Replace(sep, string.Empty);
        return s;
    }

    public async Task<EpisodeDto?> GetEpisodeAsync(
        IReadOnlyDictionary<string, string> seriesProviderIds, int? seasonNumber, int? episodeNumber,
        CancellationToken ct)
    {
        if (episodeNumber is null)
            return null;

        if (!Config.UseEmbeddedEngine)
        {
            var client = new MetaHubApiClient(_httpClientFactory);
            var series = await MetaHubMapping.ResolveAsync(client, seriesProviderIds, null, ct).ConfigureAwait(false);
            if (series is null)
                return null;

            var episodes = await client.GetEpisodesAsync(series.Id, ct).ConfigureAwait(false);
            var match = episodes.FirstOrDefault(e =>
                            e.SeasonNumber == (seasonNumber ?? 1) && e.EpisodeNumber == episodeNumber)
                        ?? episodes.FirstOrDefault(e => e.AbsoluteNumber == episodeNumber);
            if (match is not null)
                match.SeriesMediaType = series.MediaType;
            return match;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        var workId = await FindWorkIdAsync(db, seriesProviderIds, ct).ConfigureAwait(false);
        if (workId is null)
            return null;

        var mediaType = await db.Works.Where(w => w.Id == workId)
            .Select(w => w.MediaType.ToString())
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        var season = seasonNumber ?? 1;
        var episode = await db.Episodes.AsNoTracking()
                          .FirstOrDefaultAsync(e => e.SeriesWorkId == workId
                                                    && e.SeasonNumber == season
                                                    && e.EpisodeNumber == episodeNumber, ct)
                          .ConfigureAwait(false)
                      // Anime libraries often use absolute numbering.
                      ?? await db.Episodes.AsNoTracking()
                          .FirstOrDefaultAsync(e => e.SeriesWorkId == workId
                                                    && e.AbsoluteNumber == episodeNumber, ct)
                          .ConfigureAwait(false);

        if (episode is null)
            return null;

        return new EpisodeDto
        {
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            AbsoluteNumber = episode.AbsoluteNumber,
            Title = episode.Title,
            Overview = episode.Overview,
            AirDate = episode.AirDate,
            SeriesMediaType = mediaType
        };
    }

    /// <summary>Finds the work referenced by any of the item's provider ids, in preference order.</summary>
    private static async Task<Guid?> FindWorkIdAsync(
        MetaHubDbContext db, IReadOnlyDictionary<string, string> providerIds, CancellationToken ct)
    {
        foreach (var (source, id) in ProviderIdMapper.Candidates(providerIds))
        {
            if (!TryMapSource(source, out var src))
                continue;

            var workId = await db.ExternalIds
                .Where(x => x.Source == src && x.ExternalValue == id)
                .Select(x => (Guid?)x.WorkId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (workId is { } wid)
                return wid;
        }

        return null;
    }

    private static Task<Work?> LoadWorkAsync(MetaHubDbContext db, Guid id, CancellationToken ct)
        => db.Works
            .Include(w => w.ExternalIds)
            .Include(w => w.Credits).ThenInclude(c => c.Person)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    private static WorkDto ToDto(Work work, string? lang)
    {
        var overview = work.Overview;
        if (!string.IsNullOrWhiteSpace(lang) &&
            work.OverviewTranslations.TryGetValue(lang, out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
            overview = localized;

        return new WorkDto
        {
            Id = work.Id,
            MediaType = work.MediaType.ToString(),
            CanonicalTitle = work.CanonicalTitle,
            OriginalTitle = work.OriginalTitle,
            ReleaseYear = work.ReleaseYear,
            Overview = overview,
            Status = work.Status.ToString(),
            ExternalIds = work.ExternalIds
                .Select(x => new ExternalIdDto { Source = x.Source.ToString(), Value = x.ExternalValue })
                .ToList(),
            People = work.Credits
                .OrderBy(c => c.Order)
                .Where(c => c.Person is not null)
                .Select(c => new PersonDto
                {
                    Name = c.Person!.Name,
                    Role = c.Role.ToString(),
                    Character = c.Character,
                    ImageUrl = c.Person.ImageUrl,
                    Order = c.Order
                })
                .ToList()
        };
    }

    private static bool TryMapSource(string metaHubSource, out ExternalIdSource source)
    {
        source = metaHubSource switch
        {
            "anidb" => ExternalIdSource.AniDb,
            "tmdb" => ExternalIdSource.Tmdb,
            "tvdb" => ExternalIdSource.Tvdb,
            "imdb" => ExternalIdSource.Imdb,
            "anilist" => ExternalIdSource.AniList,
            "mal" => ExternalIdSource.Mal,
            "musicbrainz" => ExternalIdSource.MusicBrainz,
            "isbn" => ExternalIdSource.Isbn,
            _ => ExternalIdSource.Unknown
        };
        return source != ExternalIdSource.Unknown;
    }
}
