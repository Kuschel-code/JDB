using MediaBrowser.Controller.Entities.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;
using JellyfinImageType = MediaBrowser.Model.Entities.ImageType;

namespace MetaHub.Tests;

/// <summary>
/// Exercises the Jellyfin plugin's functions in-process: provider-id mapping, image-type
/// mapping, media-type gating, and the embedded backend (resolve + images) against SQLite.
/// </summary>
public class PluginTests
{
    // --- ProviderIdMapper ---

    [Fact]
    public void ProviderIdMapper_yields_known_sources_in_order()
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tmdb"] = "30991",
            ["AniDB"] = "23",
            ["Imdb"] = "tt0213338",
            ["Unknown"] = "x"
        };

        var candidates = ProviderIdMapper.Candidates(ids).ToList();

        // AniDB is preferred over Tmdb/Imdb; unknown keys are ignored.
        Assert.Equal(("anidb", "23"), candidates[0]);
        Assert.Contains(("tmdb", "30991"), candidates);
        Assert.Contains(("imdb", "tt0213338"), candidates);
        Assert.DoesNotContain(candidates, c => c.Id == "x");
    }

    // --- Image type mapping ---

    [Theory]
    [InlineData("poster", JellyfinImageType.Primary)]
    [InlineData("cover", JellyfinImageType.Primary)]
    [InlineData("backdrop", JellyfinImageType.Backdrop)]
    [InlineData("banner", JellyfinImageType.Banner)]
    [InlineData("logo", JellyfinImageType.Logo)]
    [InlineData("thumb", JellyfinImageType.Thumb)]
    public void ToJellyfinImageType_maps_correctly(string input, JellyfinImageType expected)
        => Assert.Equal(expected, MetaHubMapping.ToJellyfinImageType(input));

    // --- Media-type gating ---

    [Fact]
    public void IsMediaTypeEnabled_respects_config()
    {
        var config = new PluginConfiguration { EnableAnime = true, EnableMovies = false };
        Assert.True(MetaHubMapping.IsMediaTypeEnabled("Anime", config));
        Assert.False(MetaHubMapping.IsMediaTypeEnabled("Movie", config));
    }

    [Fact]
    public void ApplyProviderIds_copies_cross_ids_onto_item()
    {
        var movie = new Movie();
        var work = new WorkDto
        {
            ExternalIds =
            {
                new ExternalIdDto { Source = "Tmdb", Value = "149" },
                new ExternalIdDto { Source = "AniDb", Value = "47" }
            }
        };

        MetaHubMapping.ApplyProviderIds(movie, work);

        Assert.Equal("149", movie.ProviderIds["Tmdb"]);
        Assert.Equal("47", movie.ProviderIds["AniDB"]);
    }

    // --- Embedded backend (SQLite) ---

    private static (ServiceProvider sp, string dbPath) NewEmbedded()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-plugin-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection()
            .AddMetaHubInfrastructureSqlite(dbPath)
            .AddHttpClient()
            .BuildServiceProvider();
        sp.EnsureMetaHubSchemaCreated();
        return (sp, dbPath);
    }

    private static MetaHubBackend Backend(ServiceProvider sp, PluginConfiguration config)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            () => config);

    [Fact]
    public async Task Embedded_backend_resolves_work_and_localized_overview()
    {
        var (sp, dbPath) = NewEmbedded();
        try
        {
            var workId = Guid.NewGuid();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = new Work
                {
                    Id = workId,
                    MediaType = MediaType.Anime,
                    CanonicalTitle = "Cowboy Bebop",
                    Overview = "Bounty hunters."
                };
                work.OverviewTranslations["de"] = "Kopfgeldjäger.";
                work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "23" });
                work.Images.Add(new Image { WorkId = workId, Type = ImageType.Poster, Url = "https://img/p.jpg", Score = 90 });
                work.Images.Add(new Image { WorkId = workId, Type = ImageType.Backdrop, Url = "https://img/b.jpg", Score = 50 });
                db.Works.Add(work);
                await db.SaveChangesAsync();
            }

            var config = new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false };
            var backend = Backend(sp, config);

            var dto = await backend.ResolveAsync(
                new Dictionary<string, string> { ["AniDB"] = "23" }, "de", CancellationToken.None);

            Assert.NotNull(dto);
            Assert.Equal(workId, dto!.Id);
            Assert.Equal("Cowboy Bebop", dto.CanonicalTitle);
            Assert.Equal("Kopfgeldjäger.", dto.Overview); // localized to "de"
            Assert.Contains(dto.ExternalIds, x => x.Source == "AniDb" && x.Value == "23");

            var images = await backend.GetImagesAsync(workId, CancellationToken.None);
            Assert.Equal(2, images.Count);
            Assert.Equal("https://img/p.jpg", images[0].Url); // highest score first
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Embedded_backend_returns_null_for_unknown_ids()
    {
        var (sp, dbPath) = NewEmbedded();
        try
        {
            var backend = Backend(sp, new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });
            var dto = await backend.ResolveAsync(
                new Dictionary<string, string> { ["Tmdb"] = "999999" }, null, CancellationToken.None);
            Assert.Null(dto);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
