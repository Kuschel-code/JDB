using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
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
    public void AniList_captures_localized_titles_so_the_english_name_can_be_preferred()
    {
        // A Japanese anime whose romaji title differs from its English title — the case where
        // showing the romaji "CanonicalTitle" hides the name users actually search for.
        const string json = """
        {
          "data": {
            "Media": {
              "title": {
                "romaji": "Youkoso Jitsuryoku Shijou Shugi no Kyoushitsu e",
                "english": "Classroom of the Elite",
                "native": "ようこそ実力至上主義の教室へ"
              }
            }
          }
        }
        """;

        var data = new AniListProvider(factory: null!).Parse(json);

        Assert.Equal("Classroom of the Elite", data.TitleTranslations["en"]);
        Assert.Equal("ようこそ実力至上主義の教室へ", data.TitleTranslations["ja"]);
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

    [Fact]
    public void OpenLibrary_tolerates_empty_publisher_isbn_and_cover_arrays()
    {
        // Open Library returns empty arrays on valid 200s; FirstOrDefault().GetString() on an
        // empty array throws InvalidOperationException and would abort book enrichment.
        const string json = """
        { "title": "Some Book", "publishers": [], "isbn_13": [], "covers": [] }
        """;

        var data = new OpenLibraryProvider(factory: null!).Parse(json);

        Assert.Equal("Some Book", data.CanonicalTitle);
        Assert.Null(data.Publisher);
        Assert.Null(data.Isbn13);
        Assert.Empty(data.Images);
    }

    [Fact]
    public void Tmdb_external_id_falls_back_to_the_tmdb_movie_source()
    {
        // Anime-movie works store their TMDB id under TmdbMovie; the provider must still resolve it
        // (else enrichment silently skips them when only a movie id is present).
        var provider = new TmdbProvider(factory: null!, Options.Create(new EnrichmentOptions()));
        var work = new Work();
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.TmdbMovie, ExternalValue = "777" });

        Assert.Equal("777", provider.GetExternalId(work));
    }
}
