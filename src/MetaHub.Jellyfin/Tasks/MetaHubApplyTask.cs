using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin.Tasks;

/// <summary>
/// Scheduled task that walks the libraries and writes MetaHub's best, language-aware metadata
/// straight onto each item (title, overview, year). Unlike a normal metadata refresh — which
/// merges provider results and may keep an existing title — this overwrites a field whenever
/// MetaHub has a better value, skipping locked fields. It is the explicit "make my library match
/// MetaHub" action: an item is only touched when its current value actually differs.
/// </summary>
public class MetaHubApplyTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMetaHubBackend _backend;
    private readonly MetaHubItemGate _gate;
    private readonly ILogger<MetaHubApplyTask> _log;

    public MetaHubApplyTask(
        ILibraryManager libraryManager, IMetaHubBackend backend,
        MetaHubItemGate gate, ILogger<MetaHubApplyTask> log)
    {
        _libraryManager = libraryManager;
        _backend = backend;
        _gate = gate;
        _log = log;
    }

    public string Name => "MetaHub: Apply metadata to library";
    public string Key => "MetaHubApply";
    public string Description =>
        "Scans your libraries and replaces titles/overviews with MetaHub's best, language-aware data " +
        "where they differ. Skips locked fields. Run it after enriching to make MetaHub's titles show.";
    public string Category => "MetaHub";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
            return;

        progress.Report(0);

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series, BaseItemKind.Movie },
            Recursive = true
        });

        var total = items.Count;
        var updated = 0;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            progress.Report(total == 0 ? 100 : (double)i / total * 100);

            if (item.ProviderIds is null || item.ProviderIds.Count == 0)
                continue;

            WorkDto? work;
            try
            {
                work = await _backend.ResolveAsync(item.ProviderIds, config.PreferredLanguage, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "MetaHub apply: resolve failed for {Name}", item.Name);
                continue;
            }

            if (work is null || !_gate.IsServed(item, work.MediaType, config))
                continue;

            var current = new MetaHubItemApply.ItemFields(item.Name, item.Overview, item.ProductionYear);
            var changes = MetaHubItemApply.Compute(current, work, item.LockedFields ?? Array.Empty<MetadataField>());
            if (!changes.HasAny)
                continue;

            if (changes.Name is not null) item.Name = changes.Name;
            if (changes.Overview is not null) item.Overview = changes.Overview;
            if (changes.ProductionYear is not null) item.ProductionYear = changes.ProductionYear;

            await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
            updated++;
        }

        progress.Report(100);
        _log.LogInformation("MetaHub apply: updated {Updated} of {Total} items.", updated, total);
    }

    // Weekly, like the ingest task. Idempotent (only writes when a field differs), so re-running is cheap.
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(7).Ticks
        }
    };
}
