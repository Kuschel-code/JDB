using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Enrichment;
using MetaHub.Export;
using MetaHub.Identification;
using MetaHub.Identification.AniDb;
using MetaHub.Infrastructure;
using MetaHub.Ingest;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin;

/// <summary>
/// Wires the MetaHub engine into Jellyfin's DI so the plugin can run fully embedded: a local
/// SQLite database in the plugin data folder plus in-process ingest/identification/enrichment.
/// Engine options are taken from the plugin configuration (deferred, so config changes apply
/// after a restart). No external server or Docker is required.
/// </summary>
public class MetaHubServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // Local SQLite database in the plugin's data folder (resolved at runtime).
        services.AddMetaHubInfrastructureSqlite(sp =>
        {
            var paths = sp.GetRequiredService<IApplicationPaths>();
            var dir = Path.Combine(paths.DataPath, "metahub");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "metahub.db");
        });

        // In-process engine services.
        services.AddAnimeIngest();
        services.AddIdentification();
        services.AddEnrichment();
        services.AddNfoExport();

        // Map plugin configuration onto the engine options (read lazily from Plugin.Instance).
        services.PostConfigure<EnrichmentOptions>(o =>
        {
            var c = Plugin.Instance?.Configuration;
            if (c is null) return;
            o.TmdbApiKey = c.TmdbApiKey;
            o.GoogleBooksApiKey = c.GoogleBooksApiKey;
            o.AnnictToken = c.AnnictToken;
            o.PreferredLanguage = c.PreferredLanguage;
            if (Enum.TryParse<EnrichmentWriteMode>(c.WriteMode, true, out var mode))
                o.WriteMode = mode;
        });

        services.PostConfigure<AniDbOptions>(o =>
        {
            var c = Plugin.Instance?.Configuration;
            if (c is null) return;
            o.Enabled = c.AniDbEnabled;
            o.ClientName = c.AniDbClientName;
            o.ClientVersion = c.AniDbClientVersion;
            o.Username = c.AniDbUsername;
            o.Password = c.AniDbPassword;
        });

        // Per-item enable/disable gate (resolves library ancestry; ILibraryManager is host-provided).
        services.AddSingleton<MetaHubItemGate>();

        // Library-name -> media-type classifier (uses ILibraryManager, host-provided).
        services.AddSingleton<MetaHubLibraryClassifier>();

        // The backend the providers talk to (embedded or remote, decided per call).
        services.AddSingleton<Func<Configuration.PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);
        services.AddSingleton<IMetaHubBackend, MetaHubBackend>();

        // Creates the SQLite schema on startup when running embedded.
        services.AddHostedService<EmbeddedEngineInitializer>();
    }
}
