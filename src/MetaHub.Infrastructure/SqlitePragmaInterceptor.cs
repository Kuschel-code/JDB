using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MetaHub.Infrastructure;

/// <summary>
/// Sets <c>busy_timeout=5000</c> on every opened embedded SQLite connection so brief write
/// contention waits up to 5s instead of failing immediately with "database is locked". This is a
/// per-connection runtime setting (no write), safe even on the read-only connection EF opens to
/// probe database existence. WAL journaling is database-persistent and is enabled once in
/// <c>EnsureMetaHubSchemaCreated</c> (on a writable connection), not here.
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }
}
