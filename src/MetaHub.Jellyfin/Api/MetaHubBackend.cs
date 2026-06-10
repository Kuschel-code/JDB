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
        foreach (var (source, id) in ProviderIdMapper.Candidates(providerIds))
        {
            if (!TryMapSource(source, out var src))
                continue;

            var workId = await db.ExternalIds
                .Where(x => x.Source == src && x.ExternalValue == id)
                .Select(x => (Guid?)x.WorkId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (workId is { } wid)
            {
                work = await LoadWorkAsync(db, wid, ct).ConfigureAwait(false);
                break;
            }
        }

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
