using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Identification.Naming;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Regression for the "anime films/OVAs: correct title but no ids/overview/artwork/people" class.
/// These tests drive the REAL path the providers use — the folder is run through
/// <see cref="MetaHubMapping.FolderTitle"/> (so <see cref="FilenameParser"/> truncation is
/// exercised), the seeded work uses the genuine manami canonical ("Fate/stay night <b>Movie:</b>
/// …") with the bracketed synonym in its search titles, and the query is made with
/// <c>preferredType: Movie</c> (an anime film placed in a "Filme" library). Earlier tests that
/// passed the folder straight to ResolveByNameAsync with clean canonicals masked all of this.
/// </summary>
public class AnimeMovieResolutionTests
{
    // --- RC1: FilenameParser must not treat a directory leaf's ". subtitle" as a file extension ---

    // The parser replaces '.' with spaces while cleaning, so "I." becomes "I"; the point of RC1 is
    // that the subtitle and year survive at all (they used to be amputated at the "I." dot).
    [Theory]
    [InlineData("Fate stay night [Heaven's Feel] I. presage flower (2017)", "Fate stay night [Heaven's Feel] I presage flower", 2017)]
    [InlineData("Fate stay night [Heaven's Feel] II. lost butterfly (2019)", "Fate stay night [Heaven's Feel] II lost butterfly", 2019)]
    [InlineData("Fate stay night [Heaven's Feel] III. spring song (2020)", "Fate stay night [Heaven's Feel] III spring song", 2020)]
    public void Parse_keeps_a_roman_numeral_subtitle_and_year(string leaf, string expectedTitle, int expectedYear)
    {
        var parsed = FilenameParser.Parse(leaf);
        Assert.Equal(expectedTitle, parsed.Title);
        Assert.Equal(expectedYear, parsed.Year);
    }

    [Fact]
    public void Parse_still_strips_a_real_media_extension()
    {
        // Guard: the extension fix must not regress real video files.
        var parsed = FilenameParser.Parse("Blade.Runner.1982.2160p.mkv");
        Assert.Equal("Blade Runner", parsed.Title);
        Assert.Equal(1982, parsed.Year);
    }

    [Fact]
    public void FolderTitle_keeps_the_full_subtitle_for_a_dotted_directory_name()
    {
        var title = MetaHubMapping.FolderTitle("/Filme/Fate stay night [Heaven's Feel] II. lost butterfly (2019)");
        Assert.Equal("Fate stay night [Heaven's Feel] II lost butterfly", title);
    }

    // --- RC1+RC2+RC3 together, through the real folder path ---

    private static (ServiceProvider sp, string dbPath) NewEmbeddedWithHeavensFeelTrilogy()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-fatehf-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).AddHttpClient().BuildServiceProvider();
        sp.EnsureMetaHubSchemaCreated();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
        foreach (var (canonical, bracketed, year) in new[]
        {
            // canonical = the genuine manami title (contains "Movie:"); the bracketed form is a manami synonym.
            ("Fate/stay night Movie: Heaven's Feel - I. Presage Flower", "Fate/stay night [Heaven's Feel] I. presage flower", 2017),
            ("Fate/stay night Movie: Heaven's Feel - II. Lost Butterfly", "Fate/stay night [Heaven's Feel] II. lost butterfly", 2019),
            ("Fate/stay night Movie: Heaven's Feel - III. Spring Song", "Fate/stay night [Heaven's Feel] III. spring song", 2020),
        })
        {
            db.Works.Add(new Work
            {
                MediaType = MediaType.Anime, // every manami work is Anime, even films
                CanonicalTitle = canonical,
                SearchTitles = TitleNormalization.BuildSearchTitles(new[] { canonical, bracketed }),
                ReleaseYear = year
            });
        }
        db.SaveChanges();
        return (sp, dbPath);
    }

    private static MetaHubBackend Backend(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

    [Theory]
    [InlineData("Fate stay night [Heaven's Feel] I. presage flower (2017)", "Fate/stay night Movie: Heaven's Feel - I. Presage Flower")]
    [InlineData("Fate stay night [Heaven's Feel] II. lost butterfly (2019)", "Fate/stay night Movie: Heaven's Feel - II. Lost Butterfly")]
    [InlineData("Fate stay night [Heaven's Feel] III. spring song (2020)", "Fate/stay night Movie: Heaven's Feel - III. Spring Song")]
    public async Task HeavensFeel_film_in_a_movie_library_resolves_to_the_right_part(string leaf, string expectedCanonical)
    {
        var (sp, dbPath) = NewEmbeddedWithHeavensFeelTrilogy();
        try
        {
            // Exactly what MetaHubMovieProvider does: folder name via FolderTitle, year via the parser,
            // and preferredType = Movie because the library is named "Filme"/"Movies".
            var path = "/Filme/" + leaf;
            var name = MetaHubMapping.FolderTitle(path);
            Assert.NotNull(name);
            var year = FilenameParser.Parse(leaf).Year;

            var hit = await Backend(sp).ResolveByNameAsync(
                new[] { name! }, year, MediaType.Movie, name, null, CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal(expectedCanonical, hit!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // --- RC3 in isolation: an anime work must resolve under a Movie/Series-typed library ---

    [Fact]
    public async Task Anime_work_resolves_when_the_library_is_typed_as_movie()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-animemovie-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).AddHttpClient().BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                db.Works.Add(new Work
                {
                    MediaType = MediaType.Anime,
                    CanonicalTitle = "Ashita Sekai ga Owaru to Shitemo",
                    SearchTitles = TitleNormalization.BuildSearchTitles(new[]
                    {
                        "Ashita Sekai ga Owaru to Shitemo", "Even If the World Ends Tomorrow"
                    }),
                    ReleaseYear = 2019
                });
                await db.SaveChangesAsync();
            }

            // Folder named after the English synonym, in a "Filme" library -> preferredType Movie.
            var hit = await Backend(sp).ResolveByNameAsync(
                new[] { "Even If the World Ends Tomorrow" }, 2019, MediaType.Movie,
                "Even If the World Ends Tomorrow", null, CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal("Ashita Sekai ga Owaru to Shitemo", hit!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
