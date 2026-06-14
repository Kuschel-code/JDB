using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the title fallback chain: folder-name cleaning and the embedded backend's
/// name-based resolution (used when items carry no provider ids — e.g. fresh libraries
/// organized by folder name).
/// </summary>
public class NameFallbackTests
{
    [Theory]
    [InlineData("/Media/Anime/A Place Further Than the Universe", "A Place Further Than the Universe")]
    [InlineData("/Media/Anime/Cowboy Bebop (1998)/", "Cowboy Bebop")]
    [InlineData("/movies/Blade.Runner.1982.2160p.mkv", "Blade Runner")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void FolderTitle_cleans_last_path_segment(string? path, string? expected)
        => Assert.Equal(expected, MetaHubMapping.FolderTitle(path));

    [Fact]
    public async Task ResolveByName_matches_titles_and_disambiguates_by_year()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-name-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection()
            .AddMetaHubInfrastructureSqlite(dbPath)
            .AddHttpClient()
            .BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                db.Works.Add(new Work
                {
                    MediaType = MediaType.Anime,
                    CanonicalTitle = "A Place Further Than the Universe",
                    OriginalTitle = "宇宙よりも遠い場所",
                    ReleaseYear = 2018
                });
                // Two works sharing a title, only distinguishable by year.
                db.Works.Add(new Work { MediaType = MediaType.Anime, CanonicalTitle = "Hunter x Hunter", ReleaseYear = 1999 });
                db.Works.Add(new Work { MediaType = MediaType.Anime, CanonicalTitle = "Hunter x Hunter", ReleaseYear = 2011 });
                await db.SaveChangesAsync();
            }

            var backend = new MetaHubBackend(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

            // Case-insensitive canonical title match (the folder name). preferredType null = no type restriction.
            var byFolder = await backend.ResolveByNameAsync(
                new[] { "a place further than the universe" }, null, null, null, CancellationToken.None);
            Assert.NotNull(byFolder);
            Assert.Equal("A Place Further Than the Universe", byFolder!.CanonicalTitle);

            // Original (Japanese) title also matches.
            var byOriginal = await backend.ResolveByNameAsync(
                new[] { "宇宙よりも遠い場所" }, null, null, null, CancellationToken.None);
            Assert.NotNull(byOriginal);

            // Ambiguous title without a year → no guess.
            Assert.Null(await backend.ResolveByNameAsync(
                new[] { "Hunter x Hunter" }, null, null, null, CancellationToken.None));

            // Year disambiguates.
            var hxh = await backend.ResolveByNameAsync(
                new[] { "Hunter x Hunter" }, 2011, null, null, CancellationToken.None);
            Assert.NotNull(hxh);
            Assert.Equal(2011, hxh!.ReleaseYear);

            // Unknown name → null.
            Assert.Null(await backend.ResolveByNameAsync(
                new[] { "Does Not Exist" }, null, null, null, CancellationToken.None));
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
