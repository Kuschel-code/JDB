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
            // SQLite pools connections by default, keeping the file handle open on Windows;
            // release it before deleting the temp database.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_connections_enable_WAL_and_a_busy_timeout()
    {
        // Concurrent scan reads + on-demand enrichment writes hit the same SQLite file; without WAL
        // and a busy_timeout they raise "database is locked", which surfaces as silent metadata gaps.
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-pragma-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
            await db.Database.OpenConnectionAsync(); // routes through the connection interceptor

            var conn = db.Database.GetDbConnection();
            long busy;
            string journal;
            using (var c = conn.CreateCommand()) { c.CommandText = "PRAGMA busy_timeout;"; busy = Convert.ToInt64(c.ExecuteScalar()); }
            using (var c = conn.CreateCommand()) { c.CommandText = "PRAGMA journal_mode;"; journal = (string)c.ExecuteScalar()!; }
            await db.Database.CloseConnectionAsync();

            Assert.True(busy >= 3000, $"busy_timeout should be set; was {busy}");
            Assert.Equal("wal", journal.ToLowerInvariant());
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task OrderBy_DateTimeOffset_translates_on_sqlite()
    {
        // Regression: SQLite stores DateTimeOffset as TEXT and cannot ORDER BY it. The embedded
        // enrichment runner orders works by UpdatedAt, which previously threw at query time
        // ("SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses").
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-test-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection()
                .AddMetaHubInfrastructureSqlite(dbPath)
                .BuildServiceProvider();

            services.EnsureMetaHubSchemaCreated();

            var now = DateTimeOffset.UtcNow;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                db.Works.Add(new Work
                {
                    Id = Guid.NewGuid(), MediaType = MediaType.Anime,
                    CanonicalTitle = "Newer", Status = WorkStatus.Finished, UpdatedAt = now
                });
                db.Works.Add(new Work
                {
                    Id = Guid.NewGuid(), MediaType = MediaType.Anime,
                    CanonicalTitle = "Older", Status = WorkStatus.Finished, UpdatedAt = now.AddDays(-7)
                });
                await db.SaveChangesAsync();
            }

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                // Must translate to SQL ORDER BY (no client-side evaluation), mirroring EnrichmentRunner.
                var ordered = await db.Works.OrderBy(w => w.UpdatedAt)
                    .Select(w => w.CanonicalTitle).ToListAsync();
                Assert.Equal(new[] { "Older", "Newer" }, ordered);
            }
        }
        finally
        {
            // SQLite pools connections by default, which keeps the file handle open on
            // Windows; release it before deleting the temp database.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
