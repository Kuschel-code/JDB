using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Enrichment.Providers;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the cast/crew pipeline: provider parsing (AniList voice actors/staff, TMDB
/// cast/crew), merger upsert into Person/Credit, and delivery through the embedded backend.
/// </summary>
public class CreditTests
{
    [Fact]
    public void AniList_parses_voice_actors_and_staff()
    {
        const string json = """
        {
          "data": { "Media": {
            "title": { "romaji": "Cowboy Bebop" },
            "characters": { "edges": [
              { "node": { "name": { "full": "Spike Spiegel" }, "image": { "large": "https://img/spike.jpg" } },
                "voiceActors": [ { "name": { "full": "Koichi Yamadera" }, "image": { "large": "https://img/yamadera.jpg" } } ] }
            ] },
            "staff": { "edges": [
              { "role": "Director", "node": { "name": { "full": "Shinichiro Watanabe" }, "image": { "large": "https://img/watanabe.jpg" } } },
              { "role": "Music", "node": { "name": { "full": "Yoko Kanno" }, "image": null } },
              { "role": "Key Animation", "node": { "name": { "full": "Somebody Else" }, "image": null } }
            ] }
          } }
        }
        """;

        var data = new AniListProvider(factory: null!).Parse(json);

        var va = Assert.Single(data.Credits, c => c.Role == CreditRole.VoiceActor);
        Assert.Equal("Koichi Yamadera", va.Name);
        Assert.Equal("Spike Spiegel", va.Character);
        Assert.Equal("https://img/yamadera.jpg", va.ImageUrl);

        Assert.Contains(data.Credits, c => c.Role == CreditRole.Director && c.Name == "Shinichiro Watanabe");
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Composer && c.Name == "Yoko Kanno");
        // Unmapped staff roles come through as Unknown and are dropped by the merger.
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Unknown && c.Name == "Somebody Else");
    }

    [Fact]
    public void Tmdb_parses_cast_and_crew()
    {
        const string json = """
        {
          "title": "Akira", "release_date": "1988-07-16",
          "credits": {
            "cast": [
              { "name": "Mitsuo Iwata", "character": "Shotaro Kaneda", "order": 0, "profile_path": "/iwata.jpg" }
            ],
            "crew": [
              { "name": "Katsuhiro Otomo", "job": "Director", "profile_path": null },
              { "name": "Someone Irrelevant", "job": "Gaffer" }
            ]
          }
        }
        """;

        var data = new TmdbProvider(factory: null!, Options.Create(new EnrichmentOptions())).Parse(json);

        var actor = Assert.Single(data.Credits, c => c.Role == CreditRole.Actor);
        Assert.Equal("Mitsuo Iwata", actor.Name);
        Assert.Equal("Shotaro Kaneda", actor.Character);
        Assert.StartsWith("https://image.tmdb.org", actor.ImageUrl);

        Assert.Contains(data.Credits, c => c.Role == CreditRole.Director && c.Name == "Katsuhiro Otomo");
        Assert.DoesNotContain(data.Credits, c => c.Name == "Someone Irrelevant");
    }

    [Fact]
    public async Task Merger_upserts_people_and_credits_idempotently()
    {
        await using var db = new MetaHubDbContext(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Cowboy Bebop" };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var data = new NormalizedWorkData { Source = ExternalIdSource.AniList };
        data.Credits.Add(new NormalizedCredit
        {
            Name = "Koichi Yamadera", Role = CreditRole.VoiceActor, Character = "Spike Spiegel",
            ImageUrl = "https://img/y.jpg", Order = 0
        });
        data.Credits.Add(new NormalizedCredit
            { Name = "Shinichiro Watanabe", Role = CreditRole.Director, Order = 1 });
        data.Credits.Add(new NormalizedCredit
            { Name = "Nobody", Role = CreditRole.Unknown, Order = 2 }); // dropped

        var merger = new WorkMerger(db);
        await merger.ApplyAsync(work, new[] { data }, EnrichmentWriteMode.FillMissingOnly);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Credits.CountAsync(c => c.WorkId == work.Id));
        Assert.Equal(2, await db.People.CountAsync());
        var person = await db.People.SingleAsync(p => p.Name == "Koichi Yamadera");
        Assert.Equal("https://img/y.jpg", person.ImageUrl);

        // Re-apply: no duplicates, people reused.
        var fresh = await db.Works.Include(w => w.Credits).SingleAsync(w => w.Id == work.Id);
        await merger.ApplyAsync(fresh, new[] { data }, EnrichmentWriteMode.FillMissingOnly);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Credits.CountAsync(c => c.WorkId == work.Id));
        Assert.Equal(2, await db.People.CountAsync());
    }

    [Fact]
    public async Task Embedded_backend_delivers_people()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-cred-{Guid.NewGuid():N}.db");
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
                var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Cowboy Bebop" };
                work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "23" });
                var person = new Person { Name = "Koichi Yamadera", ImageUrl = "https://img/y.jpg" };
                db.People.Add(person);
                db.Works.Add(work);
                db.Credits.Add(new Credit
                {
                    WorkId = work.Id, Person = person, Role = CreditRole.VoiceActor,
                    Character = "Spike Spiegel", Order = 0
                });
                await db.SaveChangesAsync();
            }

            var backend = new MetaHubBackend(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

            var dto = await backend.ResolveAsync(
                new Dictionary<string, string> { ["AniDB"] = "23" }, null, CancellationToken.None);

            Assert.NotNull(dto);
            var p = Assert.Single(dto!.People);
            Assert.Equal("Koichi Yamadera", p.Name);
            Assert.Equal("VoiceActor", p.Role);
            Assert.Equal("Spike Spiegel", p.Character);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
