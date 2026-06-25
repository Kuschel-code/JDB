using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using MetaHub.Ingest.Anime;
using Xunit;

namespace MetaHub.Tests;

public class AnimeIngestServiceTests
{
    private static MetaHubDbContext NewDb() =>
        new(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ManamiDataset SampleManami() => new()
    {
        Data =
        {
            new ManamiEntry
            {
                Title = "Cowboy Bebop",
                Type = "TV",
                Episodes = 26,
                Status = "FINISHED",
                AnimeSeason = new ManamiSeason { Year = 1998 },
                Sources =
                {
                    "https://anidb.net/anime/23",
                    "https://anilist.co/anime/1",
                    "https://myanimelist.net/anime/1"
                }
            },
            new ManamiEntry
            {
                Title = "No Sources Anime",
                Episodes = 1,
                Sources = { "https://example.com/anime/999" }
            }
        }
    };

    [Fact]
    public async Task IngestManami_creates_works_and_external_ids()
    {
        await using var db = NewDb();
        var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);

        var result = await service.IngestManamiAsync(SampleManami());

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped); // entry with no recognizable sources
        Assert.Equal(3, result.ExternalIdsAdded);

        var work = await db.Works.Include(w => w.ExternalIds).Include(w => w.SeriesDetail).SingleAsync();
        Assert.Equal("Cowboy Bebop", work.CanonicalTitle);
        Assert.Equal(MediaType.Anime, work.MediaType);
        Assert.Equal(1998, work.ReleaseYear);
        Assert.Equal(WorkStatus.Finished, work.Status);
        Assert.Equal(26, work.SeriesDetail!.EpisodeCount);
        Assert.Contains(work.ExternalIds, x => x.Source == ExternalIdSource.AniDb && x.ExternalValue == "23");
    }

    [Fact]
    public async Task IngestManami_is_idempotent_on_rerun()
    {
        await using var db = NewDb();
        var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);

        await service.IngestManamiAsync(SampleManami());
        var second = await service.IngestManamiAsync(SampleManami());

        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.ExternalIdsAdded);
        Assert.Equal(1, await db.Works.CountAsync());
        Assert.Equal(3, await db.ExternalIds.CountAsync());
    }

    [Fact]
    public async Task IngestManami_stores_dataset_poster_for_works_without_art()
    {
        await using var db = NewDb();
        var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);

        var dataset = new ManamiDataset
        {
            Data =
            {
                new ManamiEntry
                {
                    Title = "Cowboy Bebop",
                    Episodes = 26,
                    Picture = "https://cdn.myanimelist.net/images/anime/4/19644.jpg",
                    Thumbnail = "https://cdn.myanimelist.net/images/anime/4/19644t.jpg",
                    Sources = { "https://anidb.net/anime/23" }
                }
            }
        };

        var first = await service.IngestManamiAsync(dataset);
        Assert.Equal(1, first.ImagesAdded);
        Assert.Equal(1, await db.Images.CountAsync(i => i.Type == ImageType.Poster && i.Source == "manami"));
        Assert.Equal(1, await db.Images.CountAsync(i => i.Type == ImageType.Thumb));

        // Re-run must not duplicate artwork.
        var second = await service.IngestManamiAsync(dataset);
        Assert.Equal(0, second.ImagesAdded);
        Assert.Equal(2, await db.Images.CountAsync());
    }

    [Fact]
    public async Task MergeFribb_adds_tvdb_tmdb_imdb_by_anidb_id()
    {
        await using var db = NewDb();
        var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);
        await service.IngestManamiAsync(SampleManami());

        var fribb = new[]
        {
            new FribbEntry { AniDbId = "23", TvdbId = "76885", TmdbId = "30991", ImdbId = "tt0213338" },
            new FribbEntry { AniDbId = "404", TvdbId = "1" } // no matching work -> skipped
        };

        var result = await service.MergeFribbAsync(fribb);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(3, result.ExternalIdsAdded);

        var work = await db.Works.Include(w => w.ExternalIds).SingleAsync();
        Assert.Contains(work.ExternalIds, x => x.Source == ExternalIdSource.Tvdb && x.ExternalValue == "76885");
        Assert.Contains(work.ExternalIds, x => x.Source == ExternalIdSource.Tmdb && x.ExternalValue == "30991");
        Assert.Contains(work.ExternalIds, x => x.Source == ExternalIdSource.Imdb && x.ExternalValue == "tt0213338");
    }

    [Fact]
    public async Task MergeFribb_keeps_overlapping_tv_and_movie_tmdb_ids_without_collision()
    {
        await using var db = NewDb();
        var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);
        // Two different anime carrying the SAME bare TMDB number but in different id spaces.
        await service.IngestManamiAsync(new ManamiDataset
        {
            Data =
            {
                new ManamiEntry { Title = "A TV Anime", Episodes = 12, Sources = { "https://anidb.net/anime/1" } },
                new ManamiEntry { Title = "A Movie Anime", Episodes = 1, Sources = { "https://anidb.net/anime/2" } }
            }
        });

        var result = await service.MergeFribbAsync(new[]
        {
            new FribbEntry { AniDbId = "1", TmdbId = "tv:26209" },
            new FribbEntry { AniDbId = "2", TmdbId = "movie:26209" }
        });

        // Both ids are kept — the namespace stops the global (Source,Value) dedup from dropping one.
        Assert.Equal(2, result.ExternalIdsAdded);

        var ids = await db.ExternalIds.Where(x => x.ExternalValue == "26209").ToListAsync();
        Assert.Contains(ids, x => x.Source == ExternalIdSource.Tmdb);      // the TV anime, bare id
        Assert.Contains(ids, x => x.Source == ExternalIdSource.TmdbMovie); // the movie anime, bare id
    }

    [Fact]
    public async Task MergeFribb_is_idempotent_for_tmdb_movie_ids_on_a_relational_db()
    {
        // InMemory ignores unique indexes; a real (SQLite) DB is needed to catch a re-run that
        // re-inserts a TmdbMovie id the cross-run dedup preload forgot to load -> UNIQUE violation.
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-fribb-rerun-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);
                await service.IngestManamiAsync(new ManamiDataset
                {
                    Data = { new ManamiEntry { Title = "A Movie Anime", Episodes = 1, Sources = { "https://anidb.net/anime/2" } } }
                });
                var first = await service.MergeFribbAsync(new[] { new FribbEntry { AniDbId = "2", TmdbId = "movie:26209" } });
                Assert.Equal(1, first.ExternalIdsAdded);
            }

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);
                // Must be idempotent: not re-insert (TmdbMovie, 26209) and hit the unique index.
                var second = await service.MergeFribbAsync(new[] { new FribbEntry { AniDbId = "2", TmdbId = "movie:26209" } });
                Assert.Equal(0, second.ExternalIdsAdded);
            }

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                Assert.Equal(1, await db.ExternalIds.CountAsync(x => x.Source == ExternalIdSource.TmdbMovie));
            }
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
