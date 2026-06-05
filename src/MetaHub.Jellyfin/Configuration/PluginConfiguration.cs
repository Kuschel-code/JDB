using MediaBrowser.Model.Plugins;

namespace MetaHub.Jellyfin.Configuration;

/// <summary>
/// Client-side settings for the MetaHub Jellyfin plugin. The plugin is only a consumer of the
/// MetaHub API, so these settings cover how Jellyfin connects to MetaHub and which content it
/// requests. Engine settings (ingest, identification, enrichment, scheduling) live on the
/// MetaHub server (appsettings.json) and are shown read-only on the plugin's "Server" tab.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // --- Connection ---

    /// <summary>Base URL of the MetaHub API the plugin talks to.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Optional API key/token for the MetaHub API (sent as the X-Api-Key header).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Per-request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    // --- Library (which content this plugin provides metadata for) ---

    public bool EnableMovies { get; set; } = true;
    public bool EnableSeries { get; set; } = true;
    public bool EnableAnime { get; set; } = true;
    public bool EnableMusic { get; set; } = true;
    public bool EnableBooks { get; set; } = true;

    /// <summary>Preferred metadata language (BCP-47), e.g. "de" or "en".</summary>
    public string PreferredLanguage { get; set; } = "de";

    /// <summary>Fall back to this language when the preferred one is missing.</summary>
    public string FallbackLanguage { get; set; } = "en";
}
