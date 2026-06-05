using MetaHub.Domain.Enums;
using MetaHub.Enrichment.Providers;
using Xunit;

namespace MetaHub.Tests;

public class EnrichmentParserTests
{
    [Fact]
    public void AniList_parses_media_fields()
    {
        const string json = """
        {
          "data": {
            "Media": {
              "title": { "romaji": "Cowboy Bebop", "english": "Cowboy Bebop", "native": "カウボーイビバップ" },
              "description": "In the year 2071...<br>humanity has colonized.",
              "status": "FINISHED",
              "startDate": { "year": 1998 },
              "episodes": 26,
              "genres": ["Action", "Sci-Fi"],
              "coverImage": { "extraLarge": "https://img/cover.jpg" },
              "bannerImage": "https://img/banner.jpg"
            }
          }
        }
        """;

        var provider = new AniListProvider(factory: null!);
        var data = provider.Parse(json);

        Assert.Equal(ExternalIdSource.AniList, data.Source);
        Assert.Equal("Cowboy Bebop", data.CanonicalTitle);
        Assert.Equal("カウボーイビバップ", data.OriginalTitle);
        Assert.Equal(1998, data.ReleaseYear);
        Assert.Equal(WorkStatus.Finished, data.Status);
        Assert.Equal(26, data.EpisodeCount);
        Assert.Contains("Action", data.Genres);
        Assert.DoesNotContain("<br>", data.Overview);
        Assert.Contains(data.Images, i => i.Type == ImageType.Poster && i.Url == "https://img/cover.jpg");
        Assert.Contains(data.Images, i => i.Type == ImageType.Backdrop);
    }

    [Fact]
    public void Jikan_parses_data_fields()
    {
        const string json = """
        {
          "data": {
            "title": "Cowboy Bebop",
            "title_english": "Cowboy Bebop",
            "title_japanese": "カウボーイビバップ",
            "synopsis": "Crime is timeless.",
            "status": "Finished Airing",
            "year": 1998,
            "episodes": 26,
            "genres": [ { "name": "Action" } ],
            "themes": [ { "name": "Space" } ],
            "images": { "jpg": { "large_image_url": "https://img/mal.jpg" } }
          }
        }
        """;

        var provider = new JikanProvider(factory: null!);
        var data = provider.Parse(json);

        Assert.Equal(ExternalIdSource.Mal, data.Source);
        Assert.Equal("Cowboy Bebop", data.CanonicalTitle);
        Assert.Equal(WorkStatus.Finished, data.Status);
        Assert.Equal(26, data.EpisodeCount);
        Assert.Contains("Action", data.Genres);
        Assert.Contains("Space", data.Genres);
        Assert.Contains(data.Images, i => i.Url == "https://img/mal.jpg");
    }
}
