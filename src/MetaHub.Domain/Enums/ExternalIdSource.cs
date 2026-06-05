namespace MetaHub.Domain.Enums;

/// <summary>
/// Known providers of external identifiers that a work can be cross-referenced to.
/// Stored as a string in the database so new sources can be added without a schema change.
/// </summary>
public enum ExternalIdSource
{
    Unknown = 0,

    // Film / Series
    Tmdb = 1,
    Tvdb = 2,
    Imdb = 3,
    Trakt = 4,

    // Anime
    AniList = 10,
    Mal = 11,
    AniDb = 12,
    Kitsu = 13,
    AnimePlanet = 14,
    Notify = 15,
    AniSearch = 16,
    LiveChart = 17,

    // Music
    MusicBrainz = 30,
    Discogs = 31,

    // Books
    Isbn = 40,
    OpenLibrary = 41,
    GoogleBooks = 42,

    // Universal bridge
    Wikidata = 50
}
