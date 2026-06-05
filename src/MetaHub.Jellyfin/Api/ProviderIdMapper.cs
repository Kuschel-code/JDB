namespace MetaHub.Jellyfin.Api;

/// <summary>
/// Maps Jellyfin provider-id keys to MetaHub <c>lookup</c> source values, in resolution order.
/// </summary>
public static class ProviderIdMapper
{
    // Jellyfin key (case-insensitive) -> MetaHub lookup source. Ordered by preference.
    private static readonly (string JellyfinKey, string MetaHubSource)[] Map =
    {
        ("AniDB", "anidb"),
        ("Tmdb", "tmdb"),
        ("Tvdb", "tvdb"),
        ("Imdb", "imdb"),
        ("AniList", "anilist"),
        ("MyAnimeList", "mal"),
        ("Mal", "mal"),
        ("MusicBrainzAlbum", "musicbrainz"),
        ("MusicBrainz", "musicbrainz"),
        ("Isbn", "isbn")
    };

    /// <summary>Yields (source, id) candidates present in <paramref name="providerIds"/>, in order.</summary>
    public static IEnumerable<(string Source, string Id)> Candidates(IReadOnlyDictionary<string, string> providerIds)
    {
        foreach (var (jellyfinKey, source) in Map)
            if (TryGet(providerIds, jellyfinKey, out var id))
                yield return (source, id);
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> ids, string key, out string value)
    {
        foreach (var kvp in ids)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
