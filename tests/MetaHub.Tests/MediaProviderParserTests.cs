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
}
