using Microsoft.Extensions.Options;
using MetaHub.Enrichment;
using MetaHub.Identification;
using MetaHub.Ingest.Anime;

namespace MetaHub.Api.Scheduling;

/// <summary>
/// Hosted background service that runs the scheduled jobs (enrichment, dataset ingest, and a
/// library scan + identification) on their configured intervals. Each job runs in its own DI
/// scope. The loop ticks once a minute and runs whatever is due.
/// </summary>
public class ScheduledScanService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchedulerOptions _options;
    private readonly ILogger<ScheduledScanService> _log;

    private DateTimeOffset _lastEnrichment = DateTimeOffset.MinValue;
    private DateTimeOffset _lastIngest = DateTimeOffset.MinValue;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;

    public ScheduledScanService(
        IServiceScopeFactory scopeFactory, IOptions<SchedulerOptions> options, ILogger<ScheduledScanService> log)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _log.LogInformation("Scheduler is disabled.");
            return;
        }

        _log.LogInformation("Scheduler started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scheduled job failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (_options.IngestEnabled && Due(_lastIngest, TimeSpan.FromHours(_options.IngestIntervalHours), now))
        {
            _lastIngest = now;
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AnimeIngestRunner>();
            _log.LogInformation("Scheduled anime ingest starting");
            await runner.RunAsync(ct);
        }

        if (_options.ScanEnabled && Due(_lastScan, TimeSpan.FromMinutes(_options.ScanIntervalMinutes), now))
        {
            _lastScan = now;
            await RunScanAsync(ct);
        }

        if (_options.EnrichmentEnabled && Due(_lastEnrichment, TimeSpan.FromMinutes(_options.EnrichmentIntervalMinutes), now))
        {
            _lastEnrichment = now;
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<EnrichmentRunner>();
            _log.LogInformation("Scheduled enrichment starting");
            await runner.EnrichAnimeAsync(
                _options.EnrichmentBatchSize, _options.EnrichmentDelayMs,
                forceRefresh: false, onlyMissing: _options.EnrichmentOnlyMissing, writeMode: null, ct);
        }
    }

    private async Task RunScanAsync(CancellationToken ct)
    {
        var files = EnumerateLibraryFiles().Take(_options.ScanMaxFilesPerRun).ToList();
        if (files.Count == 0)
            return;

        _log.LogInformation("Scheduled scan: {Count} candidate files", files.Count);
        using var scope = _scopeFactory.CreateScope();
        var identifier = scope.ServiceProvider.GetRequiredService<FileIdentificationService>();

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await identifier.IdentifyAnimeFileAsync(path, forceRehash: false, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to identify {Path}", path);
            }
        }
    }

    private IEnumerable<string> EnumerateLibraryFiles()
    {
        var extensions = _options.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _options.AnimeLibraryPaths.Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                if (extensions.Contains(Path.GetExtension(file)))
                    yield return file;
    }

    private static bool Due(DateTimeOffset last, TimeSpan interval, DateTimeOffset now) => now - last >= interval;
}
