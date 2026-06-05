using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MetaHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the MetaHub <see cref="MetaHubDbContext"/> against PostgreSQL (server mode).
    /// </summary>
    public static IServiceCollection AddMetaHubInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MetaHubDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(MetaHubDbContext).Assembly.FullName)));

        return services;
    }

    /// <summary>
    /// Registers the MetaHub <see cref="MetaHubDbContext"/> against a local SQLite file
    /// (embedded mode — no database server needed). Schema is created via EnsureCreated,
    /// so no migrations are required.
    /// </summary>
    public static IServiceCollection AddMetaHubInfrastructureSqlite(
        this IServiceCollection services, string databasePath)
    {
        services.AddDbContext<MetaHubDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        return services;
    }

    /// <summary>
    /// SQLite registration where the database path is resolved at runtime (e.g. from the
    /// Jellyfin application paths), so the file lives in the plugin's data folder.
    /// </summary>
    public static IServiceCollection AddMetaHubInfrastructureSqlite(
        this IServiceCollection services, Func<IServiceProvider, string> databasePathFactory)
    {
        services.AddDbContext<MetaHubDbContext>((sp, options) =>
            options.UseSqlite($"Data Source={databasePathFactory(sp)}"));

        return services;
    }

    /// <summary>Creates the SQLite schema if it does not exist yet (embedded mode).</summary>
    public static void EnsureMetaHubSchemaCreated(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
        db.Database.EnsureCreated();
    }
}
