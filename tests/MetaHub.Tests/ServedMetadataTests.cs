using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers metadata that is ingested + stored but used to be dropped at the serve layer. Genres
/// live in work_genres and are exported to NFO, yet the live providers never surfaced them, so an
/// item only ever got genres from other plugins. They must now flow through the served WorkDto.
/// </summary>
public class ServedMetadataTests
{
    [Fact]
    public async Task Resolved_work_carries_its_genres()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-genres-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).AddHttpClient().BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Cowboy Bebop", ReleaseYear = 1998 };
                work.WorkGenres.Add(new WorkGenre { Genre = new Genre { Name = "Action" } });
                work.WorkGenres.Add(new WorkGenre { Genre = new Genre { Name = "Sci-Fi" } });
                db.Works.Add(work);
                await db.SaveChangesAsync();
            }

            var backend = new MetaHubBackend(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

            var dto = await backend.ResolveByNameAsync(
                new[] { "Cowboy Bebop" }, null, MediaType.Anime, null, null, CancellationToken.None);

            Assert.NotNull(dto);
            Assert.Contains("Action", dto!.Genres);
            Assert.Contains("Sci-Fi", dto.Genres);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
