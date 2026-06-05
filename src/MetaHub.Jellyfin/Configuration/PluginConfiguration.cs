using MediaBrowser.Model.Plugins;

namespace MetaHub.Jellyfin.Configuration;

/// <summary>
/// Persisted settings shown in the plugin's admin settings page.
/// The page is organized into tabs (Connection / Media types / Identification / About),
/// modeled after the SkipMe.db plugin's tabbed settings UI.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // --- Connection tab ---

    /// <summary>Base URL of the MetaHub API the plugin talks to.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Optional API key/token for the MetaHub API.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Per-request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    // --- Media types tab ---

    public bool EnableMusic { get; set; } = true;
    public bool EnableMovies { get; set; } = true;
    public bool EnableSeries { get; set; } = true;
    public bool EnableAnime { get; set; } = true;
    public bool EnableBooks { get; set; } = true;

    /// <summary>Preferred metadata language (BCP-47), e.g. "de" or "en".</summary>
    public string PreferredLanguage { get; set; } = "de";

    /// <summary>Fall back to this language when the preferred one is missing.</summary>
    public string FallbackLanguage { get; set; } = "en";

    // --- Identification tab ---

    /// <summary>Compute ED2K hashes and resolve anime via AniDB (Shoko principle).</summary>
    public bool EnableEd2kIdentification { get; set; } = true;

    /// <summary>Compute acoustic fingerprints and resolve music via AcoustID.</summary>
    public bool EnableAcoustIdIdentification { get; set; } = true;

    /// <summary>Read ISBNs from book files for exact identification.</summary>
    public bool EnableIsbnIdentification { get; set; } = true;

    /// <summary>Minimum confidence [0..1] required to accept an automatic match.</summary>
    public double MinimumConfidence { get; set; } = 0.80;
}
