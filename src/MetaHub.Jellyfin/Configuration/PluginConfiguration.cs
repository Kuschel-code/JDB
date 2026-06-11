using MediaBrowser.Model.Plugins;

namespace MetaHub.Jellyfin.Configuration;

/// <summary>
/// Plugin settings. MetaHub can run in two modes:
/// <list type="bullet">
/// <item><b>Embedded</b> (default): the plugin <i>is</i> the engine — a local SQLite database,
/// in-process identification/enrichment, and Jellyfin scheduled tasks. No Docker, no server.</item>
/// <item><b>Remote</b>: the plugin is a thin client of an external MetaHub API.</item>
/// </list>
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>When true, run the whole engine inside Jellyfin (SQLite). When false, use a remote API.</summary>
    public bool UseEmbeddedEngine { get; set; } = true;

    // --- Remote mode (only used when UseEmbeddedEngine == false) ---

    public string ApiBaseUrl { get; set; } = "http://localhost:8080";
    public string ApiKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;

    // --- Library: which content + language (both modes) ---

    public bool EnableMovies { get; set; } = true;
    public bool EnableSeries { get; set; } = true;
    public bool EnableAnime { get; set; } = true;
    public bool EnableMusic { get; set; } = true;
    public bool EnableBooks { get; set; } = true;

    /// <summary>
    /// Jellyfin item GUIDs ("N" format) of libraries, series, seasons or movies for which
    /// MetaHub must NOT surface metadata/artwork. Empty = everything enabled (opt-out model).
    /// Children inherit a disabled ancestor, so only the highest disabled node is stored.
    /// </summary>
    public string[] DisabledItemIds { get; set; } = System.Array.Empty<string>();

    public string PreferredLanguage { get; set; } = "de";
    public string FallbackLanguage { get; set; } = "en";

    // --- Embedded engine: enrichment ---

    /// <summary>"FillMissingOnly" (keep existing) or "Overwrite".</summary>
    public string WriteMode { get; set; } = "FillMissingOnly";

    /// <summary>Run enrichment automatically right after a work is first matched.</summary>
    public bool EnrichOnDemand { get; set; } = true;

    /// <summary>TMDB API key (movies/series). Provider is inert without it.</summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Google Books API key (optional).</summary>
    public string GoogleBooksApiKey { get; set; } = string.Empty;

    /// <summary>Annict personal access token (annict.com) for Japanese anime metadata (optional).</summary>
    public string AnnictToken { get; set; } = string.Empty;

    // --- Embedded engine: AniDB identification (Shoko core) ---

    public bool AniDbEnabled { get; set; }
    public string AniDbClientName { get; set; } = "metahub";
    public int AniDbClientVersion { get; set; } = 1;
    public string AniDbUsername { get; set; } = string.Empty;
    public string AniDbPassword { get; set; } = string.Empty;

    // --- Embedded engine: scheduled tasks (intervals are managed by Jellyfin's task UI) ---

    /// <summary>Folders scanned for anime files during the identification task.</summary>
    public string[] AnimeLibraryPaths { get; set; } = System.Array.Empty<string>();
}
