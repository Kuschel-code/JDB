namespace MetaHub.Api.Scheduling;

/// <summary>
/// Settings for the built-in scheduler (a hosted background service). Lightweight and
/// dependency-free; swap for Hangfire/Quartz later if a dashboard or persistence is needed.
/// </summary>
public class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>Master switch for all scheduled jobs.</summary>
    public bool Enabled { get; set; }

    // Periodic enrichment of works.
    public bool EnrichmentEnabled { get; set; } = true;
    public int EnrichmentIntervalMinutes { get; set; } = 360;
    public int EnrichmentBatchSize { get; set; } = 100;
    public int EnrichmentDelayMs { get; set; } = 1000;
    /// <summary>Only enrich works that currently have no overview.</summary>
    public bool EnrichmentOnlyMissing { get; set; } = true;

    // Periodic refresh of the anime mapping datasets (manami + Fribb).
    public bool IngestEnabled { get; set; }
    public int IngestIntervalHours { get; set; } = 168; // weekly

    // Periodic library scan + identification (ED2K/AniDB) of new files.
    public bool ScanEnabled { get; set; }
    public int ScanIntervalMinutes { get; set; } = 60;
    public int ScanMaxFilesPerRun { get; set; } = 200;
    public string[] AnimeLibraryPaths { get; set; } = Array.Empty<string>();
    public string[] VideoExtensions { get; set; } = { ".mkv", ".mp4", ".avi", ".ogm", ".wmv", ".mov" };
}
