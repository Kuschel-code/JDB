using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

        // Derived from the model itself, so adding/changing a column can never be shipped
        // without triggering the rebuild (a hand-maintained constant was forgotten once and
        // left embedded installs querying columns their database did not have).
        var schemaVersion = ComputeSchemaVersion(db.Model);

        if (ReadSqliteUserVersion(db) != schemaVersion)
        {
            // Incompatible (or pre-versioning) schema — drop and rebuild from the current model.
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = {schemaVersion};");
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

    /// <summary>
    /// A stable fingerprint of everything <see cref="DatabaseFacade.EnsureCreated"/> would put
    /// into the schema: tables, columns (store type + nullability) and indexes. Ordered so the
    /// same model always yields the same string regardless of metadata enumeration order.
    /// Exposed for tests.
    /// </summary>
    internal static string BuildModelFingerprint(IModel model)
    {
        var sb = new StringBuilder();
        foreach (var entity in model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            sb.Append(entity.GetTableName() ?? entity.Name).Append('{');

            foreach (var property in entity.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                sb.Append(property.Name).Append(':')
                  // Fall back to the CLR type so a provider that reports no store type still
                  // contributes type information — otherwise int->string would go unnoticed.
                  .Append(property.GetColumnType() ?? property.ClrType.FullName)
                  .Append(property.IsNullable ? "?," : "!,");

            foreach (var index in entity.GetIndexes()
                         .Select(i => string.Join('+', i.Properties.Select(p => p.Name)) + (i.IsUnique ? "!" : ""))
                         .OrderBy(i => i, StringComparer.Ordinal))
                sb.Append("ix(").Append(index).Append(')');

            sb.Append('}');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Hashes <see cref="BuildModelFingerprint"/> into SQLite's <c>user_version</c>, which is a
    /// signed 32-bit field — so the value is masked to 31 bits, and 0 (the value a fresh database
    /// reports) is never produced.
    /// </summary>
    internal static int ComputeSchemaVersion(IModel model)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(BuildModelFingerprint(model)));
        var value = BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
        return value == 0 ? 1 : value;
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
