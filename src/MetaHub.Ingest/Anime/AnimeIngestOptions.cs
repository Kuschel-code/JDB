namespace MetaHub.Ingest.Anime;

/// <summary>
/// Configurable locations of the anime mapping datasets. Defaults point at the
/// upstream GitHub raw files; override for mirrors or pinned versions.
/// </summary>
public class AnimeIngestOptions
{
    public const string SectionName = "AnimeIngest";

    /// <summary>manami-project/anime-offline-database (minified). Published via the rolling
    /// "latest" GitHub release; the minified JSON is no longer committed to the branch tree.</summary>
    public string ManamiUrl { get; set; } =
        "https://github.com/manami-project/anime-offline-database/releases/download/latest/anime-offline-database-minified.json";

    /// <summary>Fribb/anime-lists full mapping (AniDB ↔ TVDB/TMDB/IMDb/...).</summary>
    public string FribbUrl { get; set; } =
        "https://raw.githubusercontent.com/Fribb/anime-lists/master/anime-list-full.json";

    /// <summary>
    /// kawaiioverflow/arm mapping (MAL/AniList ↔ Annict/Syoboi Calendar) — links works to the
    /// Japanese metadata databases.
    /// </summary>
    public string ArmUrl { get; set; } =
        "https://raw.githubusercontent.com/kawaiioverflow/arm/master/arm.json";

    /// <summary>Contact string used in the User-Agent, per provider etiquette.</summary>
    public string UserAgent { get; set; } = "MetaHub/0.1 (+https://github.com/kuschel-code/jdb)";
}
