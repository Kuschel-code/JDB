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

    /// <summary>
    /// Per-library media-type overrides as "&lt;libraryItemId "N"&gt;=&lt;MediaType&gt;" entries
    /// (e.g. "0c41907140d802bb58430fed7e2cd79e=Anime"). When a library is not listed, MetaHub
    /// infers the type from the library name (anime/series/movie/music/book, German + English).
    /// Used so name-based matching prefers the right work — e.g. the anime "22/7" over the US
    /// series "227" in a library named "Anime".
    /// </summary>
    public string[] LibraryTypeOverrides { get; set; } = System.Array.Empty<string>();

    public string PreferredLanguage { get; set; } = "de";
    public string FallbackLanguage { get; set; } = "en";

    /// <summary>
    /// Which title MetaHub shows as the main name:
    /// <c>Localized</c> (default: preferred language → English → canonical),
    /// <c>English</c>, <c>Original</c> (native/Japanese), or <c>Romaji</c> (the canonical/manami title).
    /// </summary>
    public string TitlePreference { get; set; } = "Localized";

    /// <summary>
    /// When true (default), if no provider id matches MetaHub resolves the work by the item's
    /// folder/file name (the path is authoritative for the season/sequel distinction). Turn off
    /// for strict id-only matching.
    /// </summary>
    public bool MatchByName { get; set; } = true;

    /// <summary>
    /// When true, the folder/file name is tried <i>before</i> provider ids (only meaningful with
    /// <see cref="MatchByName"/>). Useful when wrong/stale ids are baked into the library. Default
    /// false — provider ids win.
    /// </summary>
    public bool PreferNameOverIds { get; set; }

    // --- Embedded engine: enrichment ---

    /// <summary>"FillMissingOnly" (keep existing) or "Overwrite".</summary>
    public string WriteMode { get; set; } = "FillMissingOnly";

    /// <summary>Run enrichment automatically right after a work is first matched.</summary>
    public bool EnrichOnDemand { get; set; } = true;

    /// <summary>TMDB API key (movies/series). Provider is inert without it.</summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Google Books API key (optional).</summary>
    public string GoogleBooksApiKey { get; set; } = string.Empty;

    /// <summary>fanart.tv API key (optional): high-quality posters/backgrounds/clear logos. Free at fanart.tv.</summary>
    public string FanArtApiKey { get; set; } = string.Empty;

    // --- Embedded engine: which enrichment sources to use (all on by default) ---

    public bool EnableAniList { get; set; } = true;
    public bool EnableJikan { get; set; } = true;
    public bool EnableTmdb { get; set; } = true;
    public bool EnableKitsu { get; set; } = true;
    public bool EnableFanArt { get; set; } = true;
    public bool EnableShikimori { get; set; } = true;
    public bool EnableAnnict { get; set; } = true;

    /// <summary>Annict personal access token (annict.com) for Japanese anime metadata (optional).</summary>
    public string AnnictToken { get; set; } = string.Empty;

    // --- Embedded engine: AniDB identification (Shoko core) ---

    public bool AniDbEnabled { get; set; }
    public string AniDbClientName { get; set; } = "metahub";
    public int AniDbClientVersion { get; set; } = 1;
    public string AniDbUsername { get; set; } = string.Empty;
    public string AniDbPassword { get; set; } = string.Empty;

    /// <summary>Registered client name for the AniDB HTTP API (may differ from the UDP client).</summary>
    public string AniDbHttpClientName { get; set; } = "metahub";

    /// <summary>Registered client version for the AniDB HTTP API.</summary>
    public string AniDbHttpClientVersion { get; set; } = "1";

    /// <summary>Enable AniDB HTTP API enrichment (titles, episodes, characters).</summary>
    public bool EnableAniDb { get; set; } = true;

    // --- Embedded engine: scheduled tasks (intervals are managed by Jellyfin's task UI) ---

    /// <summary>Folders scanned for anime files during the identification task.</summary>
    public string[] AnimeLibraryPaths { get; set; } = System.Array.Empty<string>();
}
