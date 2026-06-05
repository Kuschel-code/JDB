using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using JellyfinImageType = MediaBrowser.Model.Entities.ImageType;

namespace MetaHub.Jellyfin.Api;

/// <summary>Shared mapping helpers between MetaHub DTOs and Jellyfin types.</summary>
public static class MetaHubMapping
{
    /// <summary>Resolves a MetaHub work from a Jellyfin item's provider ids.</summary>
    public static async Task<WorkDto?> ResolveAsync(
        MetaHubApiClient client, IReadOnlyDictionary<string, string> providerIds, CancellationToken ct)
    {
        foreach (var (source, id) in ProviderIdMapper.Candidates(providerIds))
        {
            var work = await client.LookupAsync(source, id, ct).ConfigureAwait(false);
            if (work is not null)
                return work;
        }

        return null;
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
