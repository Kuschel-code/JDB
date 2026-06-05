using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MetaHub.Infrastructure;

namespace MetaHub.Jellyfin;

/// <summary>
/// Creates the embedded SQLite schema on startup (when embedded mode is enabled), so the
/// plugin works out of the box without migrations or a database server.
/// </summary>
public class EmbeddedEngineInitializer : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EmbeddedEngineInitializer> _log;

    public EmbeddedEngineInitializer(IServiceProvider services, ILogger<EmbeddedEngineInitializer> log)
    {
        _services = services;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.UseEmbeddedEngine != true)
            return Task.CompletedTask;

        try
        {
            _services.EnsureMetaHubSchemaCreated();
            _log.LogInformation("MetaHub embedded SQLite database is ready.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to initialize the MetaHub embedded database.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
