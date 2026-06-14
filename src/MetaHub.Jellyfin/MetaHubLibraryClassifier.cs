using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MetaHub.Domain.Enums;
using MetaHub.Jellyfin.Configuration;

namespace MetaHub.Jellyfin;

/// <summary>
/// Works out which media type MetaHub should resolve an item as, based on the Jellyfin library
/// it lives in. Jellyfin has no native "anime" collection type (anime libraries are just
/// "tvshows"), so the signal is the library <i>name</i> — e.g. a library called "Anime" implies
/// <see cref="MediaType.Anime"/>. An explicit per-library override
/// (<see cref="PluginConfiguration.LibraryTypeOverrides"/>) wins over the name heuristic.
///
/// This lets name-based matching prefer the right work (the anime "22/7" over the US series
/// "227" for a folder named "227" in an "Anime" library).
/// </summary>
public sealed class MetaHubLibraryClassifier
{
    private readonly ILibraryManager _libraryManager;

    public MetaHubLibraryClassifier(ILibraryManager libraryManager) => _libraryManager = libraryManager;

    /// <summary>
    /// The media type expected for an item, from its library override or library-name heuristic.
    /// Null when the library is unknown or its type cannot be inferred (no constraint applied).
    /// </summary>
    public MediaType? Classify(ItemLookupInfo info, PluginConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(info.Path))
            return null;

        var overrides = ParseOverrides(config.LibraryTypeOverrides);

        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (folder.Locations is null || !folder.Locations.Any(loc => PathIsUnder(info.Path!, loc)))
                continue;

            // Explicit override (by library item id) wins.
            var id = (folder.ItemId ?? string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            if (id.Length > 0 && overrides.TryGetValue(id, out var forced))
                return forced;

            return FromLibraryName(folder.Name);
        }

        return null;
    }

    /// <summary>Heuristic mapping of a library name to a media type (German + English). Null if unclear.</summary>
    public static MediaType? FromLibraryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var n = name.ToLowerInvariant();

        // "anime" first: an "Anime" library is also a TV-shows library, but anime wins.
        if (n.Contains("anime")) return MediaType.Anime;
        if (n.Contains("serie") || n.Contains("series") || n.Contains("show") || n.Contains(" tv") || n == "tv") return MediaType.Series;
        if (n.Contains("film") || n.Contains("movie") || n.Contains("kino")) return MediaType.Movie;
        if (n.Contains("musik") || n.Contains("music") || n.Contains("audio")) return MediaType.Music;
        if (n.Contains("buch") || n.Contains("büch") || n.Contains("book") || n.Contains("manga") || n.Contains("comic")) return MediaType.Book;
        return null;
    }

    /// <summary>Parses an override value ("Anime"/"Series"/"Movie"/"Music"/"Book"); null for auto/unknown.</summary>
    public static MediaType? ParseType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "anime" => MediaType.Anime,
            "series" or "serie" => MediaType.Series,
            "movie" or "film" => MediaType.Movie,
            "music" or "musik" => MediaType.Music,
            "book" or "buch" => MediaType.Book,
            _ => null // "auto", "", anything else => no constraint
        };
    }

    private static Dictionary<string, MediaType> ParseOverrides(string[]? entries)
    {
        var map = new Dictionary<string, MediaType>(StringComparer.OrdinalIgnoreCase);
        if (entries is null)
            return map;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            var eq = entry.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = entry[..eq].Trim().Replace("-", string.Empty).ToLowerInvariant();
            if (key.Length > 0 && ParseType(entry[(eq + 1)..]) is { } type)
                map[key] = type;
        }
        return map;
    }

    private static bool PathIsUnder(string itemPath, string? libraryLocation)
    {
        if (string.IsNullOrWhiteSpace(libraryLocation))
            return false;
        var item = Norm(itemPath);
        var root = Norm(libraryLocation);
        return item == root || item.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Norm(string path)
        => path.Replace('\\', '/').TrimEnd('/');
}
