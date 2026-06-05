using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Enrichment;

namespace MetaHub.Jellyfin.Tasks;

/// <summary>
/// Scheduled task that enriches works lacking metadata (AniList/Jikan/TMDB/…) into the embedded
/// database. Runs inside Jellyfin. Default: daily.
/// </summary>
public class MetaHubEnrichTask : IScheduledTask
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MetaHubEnrichTask(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public string Name => "MetaHub: Enrich metadata";
    public string Key => "MetaHubEnrich";
    public string Description => "Fetches and merges metadata/artwork for works that have no information yet.";
    public string Category => "MetaHub";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.UseEmbeddedEngine != true)
            return;

        progress.Report(0);
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<EnrichmentRunner>();
        await runner.EnrichAnimeAsync(
            maxItems: 500, delayMs: 500, forceRefresh: false, onlyMissing: true,
            writeMode: null, ct: cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(1).Ticks
        }
    };
}
