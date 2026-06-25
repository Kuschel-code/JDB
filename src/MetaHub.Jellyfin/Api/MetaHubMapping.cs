using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using JellyfinImageType = MediaBrowser.Model.Entities.ImageType;
using PersonKind = global::Jellyfin.Data.Enums.PersonKind;
using PersonInfo = MediaBrowser.Controller.Entities.PersonInfo;

namespace MetaHub.Jellyfin.Api;

/// <summary>Shared mapping helpers between MetaHub DTOs and Jellyfin types.</summary>
public static class MetaHubMapping
{
    /// <summary>Resolves a MetaHub work from a Jellyfin item's provider ids.</summary>
    public static async Task<WorkDto?> ResolveAsync(
        MetaHubApiClient client, IReadOnlyDictionary<string, string> providerIds, string? lang, CancellationToken ct)
    {
        foreach (var (source, id) in ProviderIdMapper.Candidates(providerIds))
        {
            var work = await client.LookupAsync(source, id, lang, ct).ConfigureAwait(false);
            if (work is not null)
                return work;
        }

        return null;
    }

    /// <summary>True when the plugin is configured to provide metadata for this work's media type.</summary>
    public static bool IsMediaTypeEnabled(string mediaType, Configuration.PluginConfiguration config)
        => mediaType.ToLowerInvariant() switch
        {
            "movie" => config.EnableMovies,
            "series" => config.EnableSeries,
            "anime" => config.EnableAnime,
            "music" => config.EnableMusic,
            "book" => config.EnableBooks,
            _ => true
        };

    /// <summary>
    /// Title candidates for name-based matching: the lookup name first, then the cleaned
    /// folder/file name from the item's path (libraries are usually organized by title).
    /// </summary>
    public static IReadOnlyList<string> NameCandidates(MediaBrowser.Controller.Providers.ItemLookupInfo info)
    {
        var candidates = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(info.Name))
            candidates.Add(info.Name.Trim());
        if (FolderTitle(info.Path) is { } folder && !candidates.Contains(folder, StringComparer.OrdinalIgnoreCase))
            candidates.Add(folder);
        return candidates;
    }

    /// <summary>The cleaned title from the last path segment (folder or file name), or null.</summary>
    public static string? FolderTitle(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var leaf = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(leaf))
            return null;
        var parsed = MetaHub.Identification.Naming.FilenameParser.Parse(leaf);
        return string.IsNullOrWhiteSpace(parsed.Title) ? null : parsed.Title;
    }

    /// <summary>Adds the work's cast/crew to the metadata result (actors, directors, …).</summary>
    public static void ApplyPeople<T>(MediaBrowser.Controller.Providers.MetadataResult<T> result, WorkDto work)
        where T : BaseItem
    {
        foreach (var person in work.People)
        {
            var kind = person.Role switch
            {
                "Actor" or "VoiceActor" => PersonKind.Actor,
                "Director" => PersonKind.Director,
                "Writer" => PersonKind.Writer,
                "Author" => PersonKind.Author,
                "Producer" => PersonKind.Producer,
                "Composer" => PersonKind.Composer,
                "Artist" => PersonKind.Artist,
                _ => PersonKind.Unknown
            };
            if (kind == PersonKind.Unknown || string.IsNullOrWhiteSpace(person.Name))
                continue;

            result.AddPerson(new PersonInfo
            {
                Name = person.Name,
                Type = kind,
                Role = person.Character ?? string.Empty,
                ImageUrl = person.ImageUrl
            });
        }
    }

    /// <summary>Copies MetaHub cross-ids back onto the Jellyfin item.</summary>
    public static void ApplyProviderIds(BaseItem item, WorkDto work)
    {
        foreach (var ext in work.ExternalIds)
        {
            var key = ToJellyfinKey(ext.Source);
            if (key is not null)
                item.SetProviderId(key, ext.Value);
        }
    }

    public static JellyfinImageType ToJellyfinImageType(string metaHubType) => metaHubType.ToLowerInvariant() switch
    {
        "poster" or "cover" => JellyfinImageType.Primary,
        "backdrop" => JellyfinImageType.Backdrop,
        "banner" => JellyfinImageType.Banner,
        "logo" => JellyfinImageType.Logo,
        "thumb" => JellyfinImageType.Thumb,
        _ => JellyfinImageType.Primary
    };

    private static string? ToJellyfinKey(string metaHubSource) => metaHubSource.ToLowerInvariant() switch
    {
        "tmdb" => "Tmdb",
        "tmdbmovie" => "Tmdb", // movie ids live under TmdbMovie but map to Jellyfin's bare Tmdb key
        "tvdb" => "Tvdb",
        "imdb" => "Imdb",
        "anidb" => "AniDB",
        "anilist" => "AniList",
        "mal" => "MyAnimeList",
        "musicbrainz" => "MusicBrainzAlbum",
        "isbn" => "Isbn",
        _ => null
    };
}
