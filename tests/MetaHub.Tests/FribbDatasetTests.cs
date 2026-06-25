using System.Text.Json;
using MetaHub.Ingest.Anime;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Guards the Fribb mapping deserialization against the real anime-list-full.json shape:
/// the id field is "tvdb_id" (not "thetvdb_id"), and "themoviedb_id" is an object such as
/// {"tv": N} / {"movie": N}. These previously caused the ingest to fail at parse time.
/// </summary>
public class FribbDatasetTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserializes_real_shapes_without_throwing()
    {
        // Mirrors actual Fribb rows: numeric ids, an object-wrapped TMDB tv id, a string imdb id,
        // an object-wrapped TMDB movie id, and a row with themoviedb_id absent.
        const string json = """
        [
          { "anidb_id": 1, "anilist_id": 290, "mal_id": 290, "kitsu_id": 265,
            "tvdb_id": 72025, "themoviedb_id": { "tv": 26209 }, "imdb_id": "tt0286390" },
          { "anidb_id": 2, "themoviedb_id": { "movie": 12345 } },
          { "anidb_id": 3 }
        ]
        """;

        var entries = JsonSerializer.Deserialize<List<FribbEntry>>(json, Options);

        Assert.NotNull(entries);
        Assert.Equal(3, entries!.Count);

        var first = entries[0];
        Assert.Equal("1", first.AniDbId);
        Assert.Equal("290", first.AniListId);
        Assert.Equal("72025", first.TvdbId);   // tvdb_id, not thetvdb_id
        Assert.Equal("tv:26209", first.TmdbId); // namespaced from {"tv": 26209} — tv/movie id spaces overlap
        Assert.Equal("tt0286390", first.ImdbId);

        Assert.Equal("movie:12345", entries[1].TmdbId); // namespaced from {"movie": 12345}

        Assert.Null(entries[2].TmdbId);            // absent -> null, no throw
        Assert.Null(entries[2].TvdbId);
    }
}
