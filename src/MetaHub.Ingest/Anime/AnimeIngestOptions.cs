namespace MetaHub.Ingest.Anime;

/// <summary>
/// Configurable locations of the anime mapping datasets. Defaults point at the
/// upstream GitHub raw files; override for mirrors or pinned versions.
/// </summary>
public class AnimeIngestOptions
{
    public const string SectionName = "AnimeIngest";

    /// <summary>manami-project/anime-offline-database (minified release).</summary>
    public string ManamiUrl { get; set; } =
        "https://raw.githubusercontent.com/manami-project/anime-offline-database/master/anime-offline-database-minified.json";

    /// <summary>Fribb/anime-lists full mapping (AniDB ↔ TVDB/TMDB/IMDb/...).</summary>
    public string FribbUrl { get; set; } =
        "https://raw.githubusercontent.com/Fribb/anime-lists/master/anime-lists-full.json";

    /// <summary>Contact string used in the User-Agent, per provider etiquette.</summary>
    public string UserAgent { get; set; } = "MetaHub/0.1 (+https://github.com/kuschel-code/jdb)";
}
