namespace MetaHub.Enrichment;

/// <summary>How enrichment writes to fields that already have a value.</summary>
public enum EnrichmentWriteMode
{
    /// <summary>Only populate fields that are currently empty; never change existing data.</summary>
    FillMissingOnly = 0,
    /// <summary>Overwrite existing fields with the highest-priority provider value.</summary>
    Overwrite = 1
}

/// <summary>
/// Caching/TTL settings for enrichment. Finished works change rarely (long TTL); ongoing
/// works need fresher data (short TTL). Images are effectively permanent.
/// </summary>
public class EnrichmentOptions
{
    public const string SectionName = "Enrichment";

    public int TtlFinishedDays { get; set; } = 30;
    public int TtlOngoingDays { get; set; } = 1;

    /// <summary>
    /// Default write behavior: keep existing metadata (fill only gaps) or overwrite it.
    /// </summary>
    public EnrichmentWriteMode WriteMode { get; set; } = EnrichmentWriteMode.FillMissingOnly;

    /// <summary>Contact string used in the User-Agent for all providers.</summary>
    public string UserAgent { get; set; } = "MetaHub/0.1 (+https://github.com/kuschel-code/jdb)";

    // Provider API keys (optional; providers that need a key are skipped when it is empty).
    public string TmdbApiKey { get; set; } = string.Empty;
    public string GoogleBooksApiKey { get; set; } = string.Empty;
}
