using System.Xml.Linq;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Export;
using Xunit;

namespace MetaHub.Tests;

public class NfoBuilderTests
{
    private static Work AnimeWork()
    {
        var work = new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "Cowboy Bebop",
            OriginalTitle = "カウボーイビバップ",
            Overview = "Crime is timeless.",
            ReleaseYear = 1998,
            SeriesDetail = new SeriesDetail { EpisodeCount = 26, Network = "TV Tokyo" }
        };
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "23" });
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.Tmdb, ExternalValue = "30991" });
        work.WorkGenres.Add(new WorkGenre { Genre = new Genre { Name = "Action" } });
        work.Images.Add(new Image { Type = ImageType.Poster, Url = "https://img/p.jpg", Score = 80 });
        return work;
    }

    [Fact]
    public void BuildTvShow_emits_expected_structure()
    {
        var xml = NfoBuilder.Build(AnimeWork());
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        Assert.Equal("tvshow", root.Name.LocalName);
        Assert.Equal("Cowboy Bebop", root.Element("title")!.Value);
        Assert.Equal("カウボーイビバップ", root.Element("originaltitle")!.Value);
        Assert.Equal("1998", root.Element("year")!.Value);
        Assert.Equal("26", root.Element("episode")!.Value);
        Assert.Equal("Action", root.Element("genre")!.Value);
        Assert.Contains(root.Elements("thumb"), t => (string?)t.Attribute("aspect") == "poster");

        var anidb = root.Elements("uniqueid").Single(u => (string?)u.Attribute("type") == "anidb");
        Assert.Equal("23", anidb.Value);
        Assert.Equal("true", anidb.Attribute("default")!.Value.ToLowerInvariant());
    }

    [Fact]
    public void FileName_depends_on_media_type()
    {
        Assert.Equal("tvshow.nfo", NfoBuilder.FileNameFor(new Work { MediaType = MediaType.Anime }));
        Assert.Equal("movie.nfo", NfoBuilder.FileNameFor(new Work { MediaType = MediaType.Movie }));
        Assert.Equal("album.nfo", NfoBuilder.FileNameFor(new Work { MediaType = MediaType.Music }));
    }

    [Fact]
    public void BuildMovie_uses_movie_root_and_tmdb_default()
    {
        var work = new Work { MediaType = MediaType.Movie, CanonicalTitle = "Akira", ReleaseYear = 1988 };
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.Tmdb, ExternalValue = "149" });

        var doc = XDocument.Parse(NfoBuilder.Build(work));
        Assert.Equal("movie", doc.Root!.Name.LocalName);
        var tmdb = doc.Root.Elements("uniqueid").Single(u => (string?)u.Attribute("type") == "tmdb");
        Assert.Equal("true", tmdb.Attribute("default")!.Value.ToLowerInvariant());
    }

    [Fact]
    public void TmdbMovie_id_is_exported_as_a_tmdb_uniqueid()
    {
        // Anime-movie works store their TMDB id under TmdbMovie; it must still appear as a
        // <uniqueid type="tmdb"> in the NFO (else cross-referencing breaks for those works).
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "A Movie Anime", ReleaseYear = 2010 };
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.TmdbMovie, ExternalValue = "26209" });

        var doc = XDocument.Parse(NfoBuilder.Build(work));
        var tmdb = doc.Root!.Elements("uniqueid").Single(u => (string?)u.Attribute("type") == "tmdb");
        Assert.Equal("26209", tmdb.Value);
    }
}
