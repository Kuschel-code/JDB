using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Infrastructure;
using MetaHub.Ingest.Anime;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Exercises the embedded-mode data path against a real on-disk SQLite database (not InMemory):
/// EnsureCreated → manami ingest → Fribb merge → enrichment merge. This mirrors what the
/// Jellyfin plugin does without a server or Docker.
/// </summary>
public class EmbeddedSqlitePipelineTests
{
    [Fact]
    public async Task Ingest_then_merge_runs_on_sqlite()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-pipe-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection()
                .AddMetaHubInfrastructureSqlite(dbPath)
                .BuildServiceProvider();
            services.EnsureMetaHubSchemaCreated();

            // 1) Ingest a manami sample.
            var dataset = new ManamiDataset
            {
                Data =
                {
                    new ManamiEntry
                    {
                        Title = "Cowboy Bebop", Type = "TV", Episodes = 26, Status = "FINISHED",
                        AnimeSeason = new ManamiSeason { Year = 1998 },
                        Sources =
                        {
                            "https://anidb.net/anime/23",
                            "https://anilist.co/anime/1",
                            "https://myanimelist.net/anime/1"
                        }
                    }
                }
            };

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var ingest = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);
                var manami = await ingest.IngestManamiAsync(dataset);
                Assert.Equal(1, manami.Created);

                // 2) Merge Fribb (adds TVDB/TMDB/IMDb by AniDB id).
                var fribb = new[] { new FribbEntry { AniDbId = "23", TvdbId = "76885", TmdbId = "30991" } };
                var merge = await ingest.MergeFribbAsync(fribb);
                Assert.Equal(2, merge.ExternalIdsAdded);
            }

            // 3) Enrichment merge (normalized data → canonical work) on SQLite.
            Guid workId;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = await db.Works.Include(w => w.ExternalIds).SingleAsync();
                workId = work.Id;

                var data = new NormalizedWorkData
                {
                    Source = ExternalIdSource.AniList,
                    Overview = "Bounty hunters in space.",
                    Status = WorkStatus.Finished,
                    EpisodeCount = 26
                };
                data.Genres.Add("Action");
                data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = "https://img/p.jpg", Score = 80 });

                await new WorkMerger(db).ApplyAsync(work, new[] { data }, EnrichmentWriteMode.FillMissingOnly, "de");
                await db.SaveChangesAsync();
            }

            // 4) Verify the persisted state in SQLite.
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = await db.Works.SingleAsync();
                Assert.Equal("Bounty hunters in space.", work.Overview);
                Assert.Equal(WorkStatus.Finished, work.Status);
                Assert.Equal(5, await db.ExternalIds.CountAsync()); // anidb, anilist, mal, tvdb, tmdb
                Assert.Equal(1, await db.Images.CountAsync(i => i.WorkId == workId));
                Assert.Equal(1, await db.WorkGenres.CountAsync(wg => wg.WorkId == workId));
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
}
