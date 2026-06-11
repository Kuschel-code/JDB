using Microsoft.EntityFrameworkCore;
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
}
