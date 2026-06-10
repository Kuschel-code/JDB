using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Enrichment;
using MetaHub.Export;
using MetaHub.Identification;
using MetaHub.Identification.AniDb;
using MetaHub.Infrastructure;
using MetaHub.Ingest;
using MetaHub.Ingest.Anime;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Verifies the embedded engine's dependency graph — what the plugin's
/// IPluginServiceRegistrator wires into Jellyfin's DI — is fully constructible and every
/// engine service the plugin relies on can be resolved. (MetaHubItemGate is excluded here:
/// it depends on Jellyfin's ILibraryManager, which only the real host provides; it has its
/// own unit tests.)
/// </summary>
public class EmbeddedDiGraphTests
{
    [Fact]
    public void All_embedded_services_resolve()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-di-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging();

            // Mirror MetaHubServiceRegistrator.RegisterServices (engine part).
            services.AddMetaHubInfrastructureSqlite(dbPath);
            services.AddAnimeIngest();
            services.AddIdentification();
            services.AddEnrichment();
            services.AddNfoExport();
            services.AddSingleton<Func<PluginConfiguration>>(_ => () => new PluginConfiguration());
            services.AddSingleton<IMetaHubBackend, MetaHubBackend>();

            using var provider = services.BuildServiceProvider(validateScopes: true);
            provider.EnsureMetaHubSchemaCreated();

            // Singletons.
            Assert.NotNull(provider.GetRequiredService<IMetaHubBackend>());
            Assert.NotNull(provider.GetRequiredService<IAniDbClient>());

            // Scoped engine services.
            using var scope = provider.CreateScope();
            var sp = scope.ServiceProvider;
            Assert.NotNull(sp.GetRequiredService<MetaHubDbContext>());
            Assert.NotNull(sp.GetRequiredService<AnimeIngestRunner>());
            Assert.NotNull(sp.GetRequiredService<EnrichmentService>());
            Assert.NotNull(sp.GetRequiredService<EnrichmentRunner>());
            Assert.NotNull(sp.GetRequiredService<FileIdentificationService>());
            Assert.NotNull(sp.GetRequiredService<NfoExportService>());

            // At least the two anime metadata providers are registered.
            var providers = sp.GetServices<IMetadataProvider>().ToList();
            Assert.True(providers.Count >= 2);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
