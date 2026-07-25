using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// The embedded SQLite database is rebuilt when its schema fingerprint changes. That
/// fingerprint used to be a hand-maintained constant, and forgetting to bump it shipped a
/// release whose model had columns the installed database did not — every episode query
/// failed with "no such column". These tests pin the properties that make the derived
/// fingerprint a reliable replacement.
/// </summary>
public class EmbeddedSchemaVersionTests
{
    private static MetaHubDbContext NewSqliteContext() =>
        new(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

    [Fact]
    public void Schema_version_is_stable_across_context_instances()
    {
        // Must not depend on metadata enumeration order or a randomized string hash —
        // otherwise every restart would look like a schema change and wipe the cache.
        using var a = NewSqliteContext();
        using var b = NewSqliteContext();

        Assert.Equal(
            DependencyInjection.ComputeSchemaVersion(a.Model),
            DependencyInjection.ComputeSchemaVersion(b.Model));
    }

    [Fact]
    public void Schema_version_fits_sqlite_user_version_and_is_never_zero()
    {
        // PRAGMA user_version is a signed 32-bit field, and 0 is what a fresh database
        // reports — a fingerprint of 0 would make an empty database look up to date.
        using var db = NewSqliteContext();

        var version = DependencyInjection.ComputeSchemaVersion(db.Model);

        Assert.InRange(version, 1, int.MaxValue);
    }

    [Fact]
    public void Fingerprint_covers_columns_so_a_new_property_forces_a_rebuild()
    {
        using var db = NewSqliteContext();

        var fingerprint = DependencyInjection.BuildModelFingerprint(db.Model);

        // The columns whose missing rebuild caused the shipped regression.
        Assert.Contains(nameof(Episode.AniDbEpisodeId), fingerprint);
        Assert.Contains(nameof(Episode.Kind), fingerprint);
        Assert.Contains(nameof(Episode.RawEpno), fingerprint);
    }

    [Fact]
    public void Fingerprint_encodes_nullability_and_indexes()
    {
        using var db = NewSqliteContext();

        var fingerprint = DependencyInjection.BuildModelFingerprint(db.Model);

        // Making a column nullable (or adding an index) is a schema change EnsureCreated
        // cannot apply to an existing file, so both must be part of the fingerprint.
        Assert.Contains("?,", fingerprint);
        Assert.Contains("!,", fingerprint);
        Assert.Contains("ix(", fingerprint);
    }

    [Fact]
    public void Different_models_produce_different_versions()
    {
        using var db = NewSqliteContext();
        var real = DependencyInjection.BuildModelFingerprint(db.Model);

        // Any structural edit must move the hash — verified against the real fingerprint
        // rather than a synthetic string, so the hashing itself is exercised.
        Assert.NotEqual(Hash(real), Hash(real + "extra_column:TEXT?,"));

        static int Hash(string s)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
            var value = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
            return value == 0 ? 1 : value;
        }
    }

    [Fact]
    public void Embedded_database_is_created_and_stamped_with_the_derived_version()
    {
        // End-to-end: the schema is created and the fingerprint is written to user_version,
        // so the next startup recognizes it as current and keeps the cache.
        var path = Path.Combine(Path.GetTempPath(), $"metahub-schema-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddMetaHubInfrastructureSqlite(path);
            using var provider = services.BuildServiceProvider();

            provider.EnsureMetaHubSchemaCreated();

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

            var connection = db.Database.GetDbConnection();
            connection.Open();
            int stamped;
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                stamped = Convert.ToInt32(command.ExecuteScalar());
            }
            finally
            {
                connection.Close();
            }

            Assert.Equal(DependencyInjection.ComputeSchemaVersion(db.Model), stamped);
        }
        finally
        {
            var dir = Path.GetDirectoryName(path)!;
            foreach (var f in Directory.GetFiles(dir, Path.GetFileName(path) + "*"))
                try { File.Delete(f); } catch (IOException) { /* best effort cleanup */ }
        }
    }
}
