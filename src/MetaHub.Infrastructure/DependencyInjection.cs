using System.Data;
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
            options.UseSqlite($"Data Source={databasePath}")
                   .AddInterceptors(new SqlitePragmaInterceptor()));

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
            options.UseSqlite($"Data Source={databasePathFactory(sp)}")
                   .AddInterceptors(new SqlitePragmaInterceptor()));

        return services;
    }

    /// <summary>
    /// Schema version of the embedded SQLite database. Bump when the model changes in a way
    /// that <see cref="DatabaseFacade.EnsureCreated"/> cannot reconcile on an existing file
    /// (e.g. a column's storage type changes), so the cache is rebuilt instead of breaking.
    /// </summary>
    private const long EmbeddedSchemaVersion = 4;

    /// <summary>
    /// Creates the SQLite schema if it does not exist yet (embedded mode). The embedded
    /// database is a rebuildable cache (ingested datasets + cached enrichment), so when the
    /// schema version changes it is wiped and recreated — no migration, no durable data lost.
    /// </summary>
    public static void EnsureMetaHubSchemaCreated(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        if (!db.Database.IsSqlite())
        {
            db.Database.EnsureCreated();
            return;
        }

        if (ReadSqliteUserVersion(db) != EmbeddedSchemaVersion)
        {
            // Incompatible (or pre-versioning) schema — drop and rebuild from the current model.
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = {EmbeddedSchemaVersion};");
        }
        else
        {
            db.Database.EnsureCreated();
        }

        // Enable WAL once on a writable connection: it persists in the database header and lets the
        // concurrent scan readers and the single enrichment writer coexist without "database is
        // locked". The per-connection busy_timeout is handled by SqlitePragmaInterceptor.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    private static long ReadSqliteUserVersion(MetaHubDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }
        finally
        {
            // Release the handle before a possible EnsureDeleted so the file can be removed.
            if (wasClosed) connection.Close();
        }
    }
}
