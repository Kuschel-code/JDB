using MetaHub.Domain.Enums;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// Translates a manami "source" URL into a typed (provider, id) cross-reference.
/// Example: "https://anidb.net/anime/1" → (AniDb, "1").
/// </summary>
public static class ManamiSourceParser
{
    public static bool TryParse(string url, out ExternalIdSource source, out string value)
    {
        source = ExternalIdSource.Unknown;
        value = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        // Last non-empty path segment is the id/slug for every supported provider.
        var id = uri.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(id))
            return false;

        source = host switch
        {
            "anidb.net" => ExternalIdSource.AniDb,
            "anilist.co" => ExternalIdSource.AniList,
            "myanimelist.net" => ExternalIdSource.Mal,
            "kitsu.io" => ExternalIdSource.Kitsu,
            "kitsu.app" => ExternalIdSource.Kitsu,
            "anime-planet.com" => ExternalIdSource.AnimePlanet,
            "notify.moe" => ExternalIdSource.Notify,
            "anisearch.com" => ExternalIdSource.AniSearch,
            "livechart.me" => ExternalIdSource.LiveChart,
            _ => ExternalIdSource.Unknown
        };

        if (source == ExternalIdSource.Unknown)
            return false;

        value = id;
        return true;
    }
}
