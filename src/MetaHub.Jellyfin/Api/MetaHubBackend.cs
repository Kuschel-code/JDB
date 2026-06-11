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
        => db.Works.Include(w => w.ExternalIds).AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

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
