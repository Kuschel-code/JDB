using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Ingest.Anime;

namespace MetaHub.Jellyfin.Tasks;

/// <summary>
/// Scheduled task that refreshes the anime mapping datasets (manami + Fribb) into the embedded
/// database. Runs inside Jellyfin — no external scheduler needed. Default: weekly.
/// </summary>
public class MetaHubIngestTask : IScheduledTask
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MetaHubIngestTask(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public string Name => "MetaHub: Update anime mappings";
    public string Key => "MetaHubAnimeIngest";
    public string Description => "Downloads the manami + Fribb anime mapping datasets and updates the local MetaHub database.";
    public string Category => "MetaHub";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.UseEmbeddedEngine != true)
            return;

        progress.Report(0);
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<AnimeIngestRunner>();
        await runner.RunAsync(cancellationToken, progress).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(7).Ticks
        }
    };
}
