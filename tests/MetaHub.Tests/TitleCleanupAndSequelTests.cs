using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Identification.Naming;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Regression tests for the "two similar series" report: a broken folder name leaking a U+FFFD
/// box into the title, and a sequel folder ("… Reawakened") resolving to the base series.
/// </summary>
public class TitleCleanupAndSequelTests
{
    [Fact]
    public void Sanitize_strips_control_replacement_and_zero_width_chars()
    {
        // Folder name with a trailing replacement char (undecodable byte) + a zero-width space.
        var dirty = "The Disastrous Life of Saiki K�​";
        Assert.Equal("The Disastrous Life of Saiki K", FilenameParser.Sanitize(dirty));
    }

    [Fact]
    public void FolderTitle_cleans_broken_folder_name()
    {
        var title = MetaHubMapping.FolderTitle("/SMB/Anime/The Disastrous Life of Saiki K�");
        Assert.Equal("The Disastrous Life of Saiki K", title);
    }

    private static (ServiceProvider sp, string dbPath) NewEmbeddedWithSaikiSeries()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-sequel-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection()
            .AddMetaHubInfrastructureSqlite(dbPath)
            .AddHttpClient()
            .BuildServiceProvider();
        sp.EnsureMetaHubSchemaCreated();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
        db.Works.Add(new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "The Disastrous Life of Saiki K.",
            OriginalTitle = "Saiki Kusuo no Psi-nan",
            ReleaseYear = 2016
        });
        db.Works.Add(new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "The Disastrous Life of Saiki K. Reawakened",
            OriginalTitle = "Saiki Kusuo no Psi-nan: Psi-shidou-hen",
            ReleaseYear = 2019
        });
        db.SaveChanges();
        return (sp, dbPath);
    }

    private static MetaHubBackend Backend(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

    [Fact]
    public async Task Sequel_folder_does_not_resolve_to_base_series()
    {
        var (sp, dbPath) = NewEmbeddedWithSaikiSeries();
        try
        {
            var backend = Backend(sp);

            // The sequel folder must map to the sequel work, never the base.
            var reawakened = await backend.ResolveByNameAsync(new[] { "The Disastrous Life of Saiki K. Reawakened" }, null, MediaType.Anime, null, null, CancellationToken.None);
            Assert.NotNull(reawakened);
            Assert.Equal("The Disastrous Life of Saiki K. Reawakened", reawakened!.CanonicalTitle);

            // The base folder (even without the trailing period) must map to the base work.
            var baseWork = await backend.ResolveByNameAsync(new[] { "The Disastrous Life of Saiki K" }, null, MediaType.Anime, null, null, CancellationToken.None);
            Assert.NotNull(baseWork);
            Assert.Equal("The Disastrous Life of Saiki K.", baseWork!.CanonicalTitle);

            // The reported bug: Jellyfin hands us the *base* name as the first candidate, but the
            // folder is the sequel. The folder name is authoritative → must pick the sequel work.
            var viaFolder = await backend.ResolveByNameAsync(
                new[] { "The Disastrous Life of Saiki K." }, null, MediaType.Anime,
                "The Disastrous Life of Saiki K. Reawakened", null, CancellationToken.None);
            Assert.NotNull(viaFolder);
            Assert.Equal("The Disastrous Life of Saiki K. Reawakened", viaFolder!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Base_folder_does_not_resolve_to_sequel_when_only_sequel_exists()
    {
        // Only the sequel is in the DB; a base-named folder must NOT borrow it.
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-sequel2-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).AddHttpClient().BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                db.Works.Add(new Work
                {
                    MediaType = MediaType.Anime,
                    CanonicalTitle = "The Disastrous Life of Saiki K. Reawakened",
                    ReleaseYear = 2019
                });
                await db.SaveChangesAsync();
            }

            var result = await Backend(sp).ResolveByNameAsync(new[] { "The Disastrous Life of Saiki K" }, null, MediaType.Anime, null, null, CancellationToken.None);
            Assert.Null(result);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
