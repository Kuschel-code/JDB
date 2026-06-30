using Microsoft.Extensions.Options;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Enrichment.Providers;
using Xunit;

namespace MetaHub.Tests;

public class MediaProviderParserTests
{
    private static IOptions<EnrichmentOptions> Opts() => Options.Create(new EnrichmentOptions());

    [Fact]
    public void Tmdb_parses_movie()
    {
        const string json = """
        {
          "title": "Akira", "original_title": "アキラ",
          "overview": "Neo-Tokyo.", "release_date": "1988-07-16",
          "genres": [ { "name": "Animation" } ],
          "poster_path": "/p.jpg", "backdrop_path": "/b.jpg"
        }
        """;
        var data = new TmdbProvider(factory: null!, Opts()).Parse(json);

        Assert.Equal(ExternalIdSource.Tmdb, data.Source);
        Assert.Equal("Akira", data.CanonicalTitle);
        Assert.Equal(1988, data.ReleaseYear);
        Assert.Contains("Animation", data.Genres);
        Assert.Contains(data.Images, i => i.Type == ImageType.Poster && i.Url.EndsWith("/p.jpg"));
        Assert.Contains(data.Images, i => i.Type == ImageType.Backdrop);
    }

    [Fact]
    public void Tmdb_parses_tv()
    {
        const string json = """
        {
          "name": "Breaking Bad", "original_name": "Breaking Bad",
          "first_air_date": "2008-01-20", "number_of_episodes": 62, "number_of_seasons": 5,
          "networks": [ { "name": "AMC" } ]
        }
        """;
        var data = new TmdbProvider(factory: null!, Opts()).Parse(json);
        Assert.Equal("Breaking Bad", data.CanonicalTitle);
        Assert.Equal(2008, data.ReleaseYear);
        Assert.Equal(62, data.EpisodeCount);
        Assert.Equal(5, data.SeasonCount);
        Assert.Equal("AMC", data.Network);
    }

    [Fact]
    public void MusicBrainz_parses_release()
    {
        const string json = """
        {
          "title": "Random Access Memories", "date": "2013-05-17",
          "label-info": [ { "label": { "name": "Columbia" } } ],
          "media": [ { "track-count": 13 } ]
        }
        """;
        var data = new MusicBrainzProvider(factory: null!).Parse(json);
        Assert.Equal("Random Access Memories", data.CanonicalTitle);
        Assert.Equal(2013, data.ReleaseYear);
        Assert.Equal("Columbia", data.Label);
        Assert.Equal(13, data.TrackCount);
    }

    [Fact]
    public void OpenLibrary_parses_book()
    {
        const string json = """
        {
          "title": "Dune", "publish_date": "1965",
          "number_of_pages": 412, "publishers": ["Chilton Books"],
          "isbn_13": ["9780801950773"], "covers": [12345]
        }
        """;
        var data = new OpenLibraryProvider(factory: null!).Parse(json);
        Assert.Equal("Dune", data.CanonicalTitle);
        Assert.Equal(1965, data.ReleaseYear);
        Assert.Equal(412, data.PageCount);
        Assert.Equal("Chilton Books", data.Publisher);
        Assert.Equal("9780801950773", data.Isbn13);
        Assert.Contains(data.Images, i => i.Type == ImageType.Cover && i.Url.Contains("12345"));
    }

    [Fact]
    public void GoogleBooks_parses_search_response()
    {
        const string json = """
        {
          "items": [
            { "volumeInfo": {
                "title": "Dune", "description": "Desert planet.",
                "publishedDate": "1965-08-01", "pageCount": 412, "publisher": "Chilton",
                "categories": ["Fiction"],
                "imageLinks": { "thumbnail": "https://books/thumb.jpg" }
            } }
          ]
        }
        """;
        var data = new GoogleBooksProvider(factory: null!, Opts()).Parse(json);
        Assert.Equal("Dune", data.CanonicalTitle);
        Assert.Equal(1965, data.ReleaseYear);
        Assert.Equal(412, data.PageCount);
        Assert.Contains("Fiction", data.Genres);
        Assert.Contains(data.Images, i => i.Url == "https://books/thumb.jpg");
    }

    [Fact]
    public void AniList_parses_main_studio_as_a_studio_credit()
    {
        const string json = """
        { "data": { "Media": {
            "title": { "romaji": "Fate/stay night [Heaven's Feel]", "english": null, "native": "フェイト" },
            "genres": ["Action"],
            "studios": { "nodes": [ { "name": "ufotable" } ] }
        } } }
        """;
        var data = new AniListProvider(factory: null!).Parse(json);
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Studio && c.Name == "ufotable");
    }

    [Fact]
    public void Tmdb_parses_production_companies_as_studio_credits()
    {
        const string json = """
        { "title": "Akira", "production_companies": [ { "name": "TMS Entertainment" }, { "name": "Akira Committee" } ] }
        """;
        var data = new TmdbProvider(factory: null!, Opts()).Parse(json);
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Studio && c.Name == "TMS Entertainment");
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Studio && c.Name == "Akira Committee");
    }

    [Fact]
    public void FanArt_parses_posters_backgrounds_logos_with_language()
    {
        const string json = """
        {
          "name": "Fate/stay night", "thetvdb_id": "439228",
          "tvposter": [ { "id": "1", "url": "https://fanart/poster.jpg", "lang": "de", "likes": "7" } ],
          "showbackground": [ { "id": "2", "url": "https://fanart/bg.jpg", "lang": "", "likes": "3" } ],
          "hdtvlogo": [ { "id": "3", "url": "https://fanart/logo.png", "lang": "en", "likes": "2" } ]
        }
        """;
        var data = new FanArtTvProvider(factory: null!, Opts()).Parse(json);

        Assert.Equal(ExternalIdSource.FanArtTv, data.Source);
        Assert.Contains(data.Images, i => i.Type == ImageType.Poster && i.Url.EndsWith("poster.jpg") && i.Lang == "de");
        Assert.Contains(data.Images, i => i.Type == ImageType.Backdrop && i.Lang == null); // "" -> neutral
        Assert.Contains(data.Images, i => i.Type == ImageType.Logo && i.Url.EndsWith("logo.png") && i.Lang == "en");
    }

    [Fact]
    public void Shikimori_parses_titles_genres_year_poster_and_keeps_russian_as_translation()
    {
        const string json = """
        {
          "id": 25537,
          "name": "Fate/stay night Movie: Heaven's Feel - I. Presage Flower",
          "japanese": [ "Gekijouban Fate/stay night Heaven's Feel I" ],
          "kind": "movie", "episodes": 1, "aired_on": "2017-10-14",
          "description": "Pervyy film [character=1]Shirou[/character] adaptatsii.",
          "image": { "original": "/system/animes/original/25537.jpg?171" },
          "genres": [ { "name": "Action", "russian": "Ekshen" }, { "name": "Fantasy", "russian": "Fentezi" } ]
        }
        """;
        var data = new ShikimoriProvider(factory: null!).Parse(json);

        Assert.Equal(ExternalIdSource.Shikimori, data.Source);
        Assert.Equal("Fate/stay night Movie: Heaven's Feel - I. Presage Flower", data.CanonicalTitle);
        Assert.Equal("Gekijouban Fate/stay night Heaven's Feel I", data.OriginalTitle);
        Assert.Equal(2017, data.ReleaseYear);
        Assert.Equal(1, data.EpisodeCount);
        Assert.Contains("Action", data.Genres);
        Assert.Contains("Fantasy", data.Genres);
        Assert.Contains(data.Images, i => i.Type == ImageType.Poster
            && i.Url == "https://shikimori.one/system/animes/original/25537.jpg?171");
        // Russian goes to a translation, never the default overview, and BBCode is stripped.
        Assert.Null(data.Overview);
        Assert.Contains("Pervyy film", data.OverviewTranslations["ru"]);
        Assert.DoesNotContain("[character", data.OverviewTranslations["ru"]);
    }
}
