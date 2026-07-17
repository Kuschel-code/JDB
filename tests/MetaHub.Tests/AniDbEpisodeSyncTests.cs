using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Identification.AniDb;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

public class AniDbEpisodeSyncTests
{
    private const string SampleAnimeXml = """
        <anime id="1">
          <titles><title xml:lang="x-jat" type="main">Cowboy Bebop</title></titles>
          <episodes>
            <episode id="100">
              <epno type="1">1</epno>
              <title xml:lang="en">Asteroid Blues</title>
              <title xml:lang="ja">アステロイド・ブルース</title>
              <airdate>1998-04-03</airdate>
            </episode>
            <episode id="101">
              <epno type="1">2</epno>
              <title xml:lang="en">Stray Dog Strut</title>
            </episode>
            <episode id="200">
              <epno type="2">S1</epno>
              <title xml:lang="en">Special Interview</title>
            </episode>
            <episode id="300">
              <epno type="3">C1</epno>
              <title xml:lang="en">Opening Credits</title>
            </episode>
          </episodes>
        </anime>
        """;

    private static MetaHubDbContext NewDb() =>
        new(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Work> SeedWorkWithPayloadAsync(MetaHubDbContext db)
    {
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Cowboy Bebop" };
        db.Works.Add(work);
        db.RawPayloads.Add(new RawPayload
        {
            WorkId = work.Id,
            Source = ExternalIdSource.AniDb,
            HttpStatus = 200,
            Body = JsonSerializer.Serialize(AniDbAnimeParser.Parse(SampleAnimeXml))
        });
        await db.SaveChangesAsync();
        return work;
    }

    [Fact]
    public async Task Sync_writes_regular_and_non_regular_episodes()
    {
        await using var db = NewDb();
        var work = await SeedWorkWithPayloadAsync(db);

        var count = await new AniDbEpisodeSync(NullLogger<AniDbEpisodeSync>.Instance)
            .SyncAsync(db, work, "1", preferredLanguage: null);

        Assert.Equal(4, count);
        var episodes = await db.Episodes.Where(e => e.SeriesWorkId == work.Id).ToListAsync();
        Assert.Equal(4, episodes.Count);

        var ep1 = episodes.Single(e => e.AniDbEpisodeId == 100);
        Assert.Equal(1, ep1.SeasonNumber);
        Assert.Equal(1, ep1.EpisodeNumber);
        Assert.Equal(1, ep1.AbsoluteNumber);
        Assert.Equal(EpisodeKind.Regular, ep1.Kind);
        Assert.Equal("1", ep1.RawEpno);
        Assert.Equal("Asteroid Blues", ep1.Title);
        Assert.Equal(new DateOnly(1998, 4, 3), ep1.AirDate);
    }

    [Fact]
    public async Task Sync_keeps_special_and_credit_with_same_number_as_distinct_rows()
    {
        // S1 (Special) and C1 (Credit) both map to season 0, number 1 — the upsert key must
        // include the kind or the later one silently overwrites the earlier one.
        await using var db = NewDb();
        var work = await SeedWorkWithPayloadAsync(db);

        await new AniDbEpisodeSync(NullLogger<AniDbEpisodeSync>.Instance)
            .SyncAsync(db, work, "1", preferredLanguage: null);

        var seasonZero = await db.Episodes
            .Where(e => e.SeriesWorkId == work.Id && e.SeasonNumber == 0)
            .ToListAsync();

        Assert.Equal(2, seasonZero.Count);
        Assert.Contains(seasonZero, e => e.Kind == EpisodeKind.Special && e.RawEpno == "S1" && e.AniDbEpisodeId == 200);
        Assert.Contains(seasonZero, e => e.Kind == EpisodeKind.Credit && e.RawEpno == "C1" && e.AniDbEpisodeId == 300);
        Assert.All(seasonZero, e => Assert.Null(e.AbsoluteNumber));
    }

    [Fact]
    public async Task Sync_is_idempotent()
    {
        await using var db = NewDb();
        var work = await SeedWorkWithPayloadAsync(db);
        var sync = new AniDbEpisodeSync(NullLogger<AniDbEpisodeSync>.Instance);

        await sync.SyncAsync(db, work, "1", preferredLanguage: null);
        await sync.SyncAsync(db, work, "1", preferredLanguage: null);

        Assert.Equal(4, await db.Episodes.CountAsync(e => e.SeriesWorkId == work.Id));
    }

    [Fact]
    public async Task Sync_updates_existing_episode_rows_in_place()
    {
        await using var db = NewDb();
        var work = await SeedWorkWithPayloadAsync(db);

        // Simulate a Jikan-created row for the same episode (season 1, number 1, Regular).
        db.Episodes.Add(new Episode
        {
            SeriesWorkId = work.Id,
            SeasonNumber = 1,
            EpisodeNumber = 1,
            Title = "Jikan Title"
        });
        await db.SaveChangesAsync();

        await new AniDbEpisodeSync(NullLogger<AniDbEpisodeSync>.Instance)
            .SyncAsync(db, work, "1", preferredLanguage: null);

        var rows = await db.Episodes
            .Where(e => e.SeriesWorkId == work.Id && e.SeasonNumber == 1 && e.EpisodeNumber == 1)
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(100, row.AniDbEpisodeId);
        Assert.Equal("1", row.RawEpno);
    }

    [Fact]
    public async Task Sync_prefers_japanese_titles_when_language_is_ja()
    {
        await using var db = NewDb();
        var work = await SeedWorkWithPayloadAsync(db);

        await new AniDbEpisodeSync(NullLogger<AniDbEpisodeSync>.Instance)
            .SyncAsync(db, work, "1", preferredLanguage: "ja");

        var ep1 = await db.Episodes.SingleAsync(e => e.AniDbEpisodeId == 100);
        Assert.Equal("アステロイド・ブルース", ep1.Title);
    }

    [Fact]
    public async Task Sync_returns_zero_without_cached_payload()
    {
        await using var db = NewDb();
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "No Payload" };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var count = await new AniDbEpisodeSync(NullLogger<AniDbEpisodeSync>.Instance)
            .SyncAsync(db, work, "1", preferredLanguage: null);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.Episodes.CountAsync());
    }
}
