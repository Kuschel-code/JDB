namespace MetaHub.Enrichment;

/// <summary>
/// Caching/TTL settings for enrichment. Finished works change rarely (long TTL); ongoing
/// works need fresher data (short TTL). Images are effectively permanent.
/// </summary>
public class EnrichmentOptions
{
    public const string SectionName = "Enrichment";

    public int TtlFinishedDays { get; set; } = 30;
    public int TtlOngoingDays { get; set; } = 1;

    /// <summary>Contact string used in the User-Agent for all providers.</summary>
    public string UserAgent { get; set; } = "MetaHub/0.1 (+https://github.com/kuschel-code/jdb)";
}
