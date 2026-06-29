using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Regression for the live "two Saiki K." report: a sequel whose distinguishing marker
/// ("Reawakened") lives ONLY in a synonym, while its canonical title is the romaji
/// ("Saiki Kusuo no Psi-nan: Psi-shidou-hen") with no marker. The sequel guard used to judge a
/// candidate only by its canonical/original SequelKey, so it dropped the correct sequel work even
/// though the folder matched its synonym exactly — and the item fell back to the base season's ids.
/// (TitleCleanupAndSequelTests masks this by seeding an English canonical that already contains
/// "Reawakened".)
/// </summary>
public class SequelSynonymTests
{
    private static (ServiceProvider sp, string dbPath) NewEmbeddedWithSaiki()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-saiki-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).AddHttpClient().BuildServiceProvider();
        sp.EnsureMetaHubSchemaCreated();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
        // Base season 1 — its synonym is the bare English name.
        db.Works.Add(new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "Saiki Kusuo no Psi-nan",
            ReleaseYear = 2016,
            SearchTitles = TitleNormalization.BuildSearchTitles(new[]
            {
                "Saiki Kusuo no Psi-nan", "The Disastrous Life of Saiki K."
            })
        });
        // The "Reawakened" continuation — marker is ONLY in the synonyms, canonical is romaji.
        db.Works.Add(new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "Saiki Kusuo no Psi-nan: Psi-shidou-hen",
            ReleaseYear = 2019,
            SearchTitles = TitleNormalization.BuildSearchTitles(new[]
            {
                "Saiki Kusuo no Psi-nan: Psi-shidou-hen",
                "The Disastrous Life of Saiki K.: Reawakened",
                "The Disastrous Life of Saiki K. - Reawakened",
                "The Disastrous Life of Saiki K." // base name is also a Reawakened synonym in manami
            })
        });
        db.SaveChanges();
        return (sp, dbPath);
    }

    private static MetaHubBackend Backend(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

    [Fact]
    public async Task Reawakened_folder_resolves_to_the_continuation_not_the_base()
    {
        var (sp, dbPath) = NewEmbeddedWithSaiki();
        try
        {
            // Jellyfin hands the base name as info.Name (the item was previously stamped as base),
            // while the folder carries the "Reawakened" marker.
            var folder = MetaHubMapping.FolderTitle("/SMB/Anime/The Disastrous Life of Saiki K. Reawakened");
            Assert.NotNull(folder);

            var hit = await Backend(sp).ResolveByNameAsync(
                new[] { "The Disastrous Life of Saiki K.", folder! },
                null, MediaType.Anime, folder, null, CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal("Saiki Kusuo no Psi-nan: Psi-shidou-hen", hit!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Base_folder_still_resolves_to_the_base_season()
    {
        var (sp, dbPath) = NewEmbeddedWithSaiki();
        try
        {
            // The base folder (no marker) must still land on season 1, disambiguated by year.
            var folder = MetaHubMapping.FolderTitle("/SMB/Anime/The Disastrous Life of Saiki K");
            Assert.NotNull(folder);

            var hit = await Backend(sp).ResolveByNameAsync(
                new[] { folder! }, 2016, MediaType.Anime, folder, null, CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal("Saiki Kusuo no Psi-nan", hit!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
