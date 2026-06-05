using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Verifies the provider-agnostic model works on SQLite (embedded plugin mode) via
/// EnsureCreated — including the JSON dictionary converters and string-keyed indexes.
/// </summary>
public class SqliteSchemaTests
{
    [Fact]
    public async Task EnsureCreated_then_roundtrip_work_with_translations()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-test-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection()
                .AddMetaHubInfrastructureSqlite(dbPath)
                .BuildServiceProvider();

            services.EnsureMetaHubSchemaCreated();

            var workId = Guid.NewGuid();
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = new Work
                {
                    Id = workId,
                    MediaType = MediaType.Anime,
                    CanonicalTitle = "Cowboy Bebop",
                    Status = WorkStatus.Finished
                };
                work.OverviewTranslations["de"] = "Kopfgeldjäger im Weltraum.";
                work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "23" });
                db.Works.Add(work);
                await db.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = await db.Works.Include(w => w.ExternalIds).SingleAsync();
                Assert.Equal("Cowboy Bebop", work.CanonicalTitle);
                Assert.Equal(MediaType.Anime, work.MediaType);
                Assert.Equal("Kopfgeldjäger im Weltraum.", work.OverviewTranslations["de"]);
                Assert.Contains(work.ExternalIds, x => x.Source == ExternalIdSource.AniDb && x.ExternalValue == "23");
            }
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
