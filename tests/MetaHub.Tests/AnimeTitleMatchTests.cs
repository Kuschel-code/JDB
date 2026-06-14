using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Proves the punctuation-insensitive, type-restricted title match (the SQL behind
/// MetaHubBackend.ResolveByNameAsync) against a real SQLite database: a folder named "227" must
/// resolve to the anime "22/7" in an Anime library, not the US series "227". The Replace() chain
/// here mirrors the one in ResolveByNameAsync and verifies EF Core translates it to SQLite.
/// </summary>
public class AnimeTitleMatchTests
{
    private const string Search = "227"; // normalized form of the folder name (already separator-free)

    [Fact]
    public async Task Type_filter_picks_the_anime_over_the_same_named_series()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-match-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).BuildServiceProvider();
            services.EnsureMetaHubSchemaCreated();

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                db.Works.Add(new Work { Id = Guid.NewGuid(), MediaType = MediaType.Anime, CanonicalTitle = "22/7", Status = WorkStatus.Finished });
                db.Works.Add(new Work { Id = Guid.NewGuid(), MediaType = MediaType.Series, CanonicalTitle = "227", Status = WorkStatus.Finished });
                await db.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

                // Without a type filter the fuzzy match finds both the anime and the series.
                var anyType = await Fuzzy(db.Works).Select(w => w.CanonicalTitle).ToListAsync();
                Assert.Equal(2, anyType.Count);
                Assert.Contains("22/7", anyType);
                Assert.Contains("227", anyType);

                // In the Anime library only the anime "22/7" remains.
                var anime = await Fuzzy(db.Works.Where(w => w.MediaType == MediaType.Anime))
                    .Select(w => w.CanonicalTitle).ToListAsync();
                Assert.Equal(new[] { "22/7" }, anime);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // Mirrors the exact/punctuation-insensitive predicate of MetaHubBackend.ResolveByNameAsync.
    private static IQueryable<Work> Fuzzy(IQueryable<Work> source) => source.Where(w =>
        w.CanonicalTitle.ToLower() == Search
        || w.CanonicalTitle.ToLower()
            .Replace(" ", "").Replace("/", "").Replace("-", "").Replace(":", "")
            .Replace(".", "").Replace(",", "").Replace("'", "").Replace("!", "")
            .Replace("?", "").Replace("_", "") == Search);
}
