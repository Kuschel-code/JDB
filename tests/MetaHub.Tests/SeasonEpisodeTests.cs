using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Enrichment.Providers;
using MetaHub.Infrastructure;
using MetaHub.Ingest.Anime;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using MetaHub.Jellyfin.Providers;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the season/episode support added for Jellyfin (providers + backend) and the
/// Japanese metadata sources (Jikan episode sync parsing, ARM mapping, Annict parsing).
/// </summary>
public class SeasonEpisodeTests
{
    // --- Jikan episode page parsing ---

    [Fact]
    public void JikanEpisodeSync_parses_page_with_japanese_titles()
    {
        const string json = """
        {
          "pagination": { "has_next_page": true },
          "data": [
            { "mal_id": 1, "title": "Asteroid Blues", "title_japanese": "アステロイド・ブルース",
              "aired": "1998-10-24T00:00:00+00:00" },
            { "mal_id": 2, "title": "Stray Dog Strut", "title_japanese": null, "aired": null }
          ]
        }
        """;

        var (episodes, hasNext) = JikanEpisodeSync.ParseEpisodesPage(json);

        Assert.True(hasNext);
        Assert.Equal(2, episodes.Count);
        Assert.Equal(1, episodes[0].Number);
        Assert.Equal("Asteroid Blues", episodes[0].Title);
        Assert.Equal("アステロイド・ブルース", episodes[0].TitleJapanese);
        Assert.Equal(new DateOnly(1998, 10, 24), episodes[0].AirDate);
        Assert.Null(episodes[1].AirDate);
    }

    // --- ARM (Japanese db) mapping merge ---

    [Fact]
    public async Task MergeArm_links_annict_and_syobocal_by_mal_or_anilist()
    {
        await using var db = new MetaHubDbContext(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Cowboy Bebop" };
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.Mal, ExternalValue = "1" });
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var service = new AnimeIngestService(db, NullLogger<AnimeIngestService>.Instance);
        var result = await service.MergeArmAsync(new[]
        {
            new ArmEntry { MalId = 1, AnnictId = 363, SyobocalTid = 538 },
            new ArmEntry { MalId = 999999, AnnictId = 1 } // unknown -> skipped
        });

        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.ExternalIdsAdded);
        Assert.Equal(1, result.Skipped);

        var saved = await db.Works.Include(w => w.ExternalIds).SingleAsync();
        Assert.Contains(saved.ExternalIds, x => x.Source == ExternalIdSource.Annict && x.ExternalValue == "363");
        Assert.Contains(saved.ExternalIds, x => x.Source == ExternalIdSource.Syobocal && x.ExternalValue == "538");

        // Re-run is idempotent.
        var again = await service.MergeArmAsync(new[] { new ArmEntry { MalId = 1, AnnictId = 363 } });
        Assert.Equal(0, again.ExternalIdsAdded);
    }

    // --- Annict (Japanese database) parsing ---

    [Fact]
    public void Annict_parses_japanese_work()
    {
        const string json = """
        {
          "data": { "searchWorks": { "nodes": [
            { "title": "カウボーイビバップ", "titleKana": "かうぼーいびばっぷ", "titleEn": "Cowboy Bebop",
              "seasonYear": 1998, "episodesCount": 26,
              "image": { "recommendedImageUrl": "https://annict/img.jpg" } }
          ] } }
        }
        """;

        var provider = new AnnictProvider(factory: null!,
            Microsoft.Extensions.Options.Options.Create(new EnrichmentOptions()));
        var data = provider.Parse(json);

        Assert.Equal(ExternalIdSource.Annict, data.Source);
        Assert.Equal("カウボーイビバップ", data.OriginalTitle);
        Assert.Equal("Cowboy Bebop", data.CanonicalTitle);
        Assert.Equal(1998, data.ReleaseYear);
        Assert.Equal(26, data.EpisodeCount);
        Assert.Contains(data.Images, i => i.Url == "https://annict/img.jpg" && i.Source == "annict");
    }

    // --- Season naming ---

    [Theory]
    [InlineData(1, "de", "Staffel 1")]
    [InlineData(2, "en", "Season 2")]
    [InlineData(0, "de", "Extras")]
    [InlineData(0, "en", "Specials")]
    [InlineData(null, "en", "Specials")]
    public void SeasonName_localizes(int? number, string lang, string expected)
        => Assert.Equal(expected, MetaHubSeasonProvider.SeasonName(number, lang));

    // --- Embedded backend episode resolution (SQLite) ---

    [Fact]
    public async Task Backend_resolves_episode_by_season_and_absolute_number()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-ep-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection()
            .AddMetaHubInfrastructureSqlite(dbPath)
            .AddHttpClient()
            .BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();

            Guid workId;
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Cowboy Bebop" };
                work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "23" });
                work.SeriesDetail = new SeriesDetail { WorkId = work.Id, EpisodeCount = 26 };
                db.Works.Add(work);
                db.Episodes.Add(new Episode
                {
                    SeriesWorkId = work.Id, SeasonNumber = 1, EpisodeNumber = 5, AbsoluteNumber = 5,
                    Title = "Ballad of Fallen Angels", AirDate = new DateOnly(1998, 11, 14)
                });
                await db.SaveChangesAsync();
                workId = work.Id;
            }

            var backend = new MetaHubBackend(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

            var ids = new Dictionary<string, string> { ["AniDB"] = "23" };

            // Season+episode match.
            var ep = await backend.GetEpisodeAsync(ids, 1, 5, CancellationToken.None);
            Assert.NotNull(ep);
            Assert.Equal("Ballad of Fallen Angels", ep!.Title);
            Assert.Equal("Anime", ep.SeriesMediaType);
            Assert.Equal(new DateOnly(1998, 11, 14), ep.AirDate);

            // Absolute-number fallback (no season info from the library).
            var byAbs = await backend.GetEpisodeAsync(ids, null, 5, CancellationToken.None);
            Assert.NotNull(byAbs);

            // Unknown episode.
            Assert.Null(await backend.GetEpisodeAsync(ids, 1, 99, CancellationToken.None));
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
