using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment.Providers;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Parses the real kitsu.app JSON:API anime shape. Kitsu is the only enrichable cross-id for some
/// manami entries (e.g. "The Red Turtle", linked only to anime-planet + kitsu), so this provider is
/// what gives those works an overview, year and artwork.
/// </summary>
public class KitsuProviderTests
{
    // Trimmed from the live kitsu.app/api/edge/anime/11621 (The Red Turtle) response, with the
    // Japanese titles filled in to exercise the title mapping.
    private const string RedTurtle = """
    {
      "data": {
        "id": "11621",
        "type": "anime",
        "attributes": {
          "canonicalTitle": "The Red Turtle",
          "titles": { "en": "The Red Turtle", "en_jp": "Red Turtle: Aru Shima no Monogatari", "ja_jp": "レッドタートル ある島の物語" },
          "synopsis": "A co-production between French studio Wild Bunch and Studio Ghibli.",
          "startDate": "2016-09-01",
          "episodeCount": 1,
          "posterImage": { "original": "https://media.kitsu.app/anime/poster_images/11621/original.jpg" },
          "coverImage": { "original": "https://media.kitsu.app/anime/cover_images/11621/original.jpeg" }
        }
      }
    }
    """;

    [Fact]
    public void Parse_reads_titles_overview_year_episodes_and_artwork()
    {
        var p = new KitsuProvider(new DummyHttpClientFactory());
        var d = p.Parse(RedTurtle);

        Assert.Equal(ExternalIdSource.Kitsu, d.Source);
        Assert.Equal("The Red Turtle", d.CanonicalTitle);
        Assert.Equal("レッドタートル ある島の物語", d.OriginalTitle);
        Assert.Equal("The Red Turtle", d.TitleTranslations["en"]);
        Assert.Equal("レッドタートル ある島の物語", d.TitleTranslations["ja"]);
        Assert.Contains("Ghibli", d.Overview);
        Assert.Equal(2016, d.ReleaseYear);
        Assert.Equal(1, d.EpisodeCount);
        Assert.Contains(d.Images, i => i.Type == ImageType.Poster && i.Url.Contains("poster_images"));
        Assert.Contains(d.Images, i => i.Type == ImageType.Backdrop && i.Url.Contains("cover_images"));
    }

    [Fact]
    public void Parse_tolerates_minimal_payload_without_throwing()
    {
        var p = new KitsuProvider(new DummyHttpClientFactory());
        var d = p.Parse("""{ "data": { "attributes": { "canonicalTitle": "X" } } }""");
        Assert.Equal("X", d.CanonicalTitle);
        Assert.Null(d.ReleaseYear);
        Assert.Empty(d.Images);
    }

    [Fact]
    public void GetExternalId_returns_the_kitsu_id_or_null()
    {
        var p = new KitsuProvider(new DummyHttpClientFactory());

        var withKitsu = new Work { MediaType = MediaType.Anime, CanonicalTitle = "X" };
        withKitsu.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.Kitsu, ExternalValue = "11621" });
        Assert.Equal("11621", p.GetExternalId(withKitsu));

        var withoutKitsu = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Y" };
        withoutKitsu.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniList, ExternalValue = "1" });
        Assert.Null(p.GetExternalId(withoutKitsu));
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
