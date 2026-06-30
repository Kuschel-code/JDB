using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Robustness of the enrichment loop: one provider's network/parse failure must not abort the whole
/// work and discard the other providers' data (the fetch used to run outside the per-provider catch).
/// </summary>
public class EnrichmentServiceTests
{
    private sealed class FakeProvider : IMetadataProvider
    {
        public ExternalIdSource Source { get; init; }
        public int Priority { get; init; }
        public Func<string?> OnFetch { get; init; } = () => "{}";
        public NormalizedWorkData? Parsed { get; init; }

        public string? GetExternalId(Work work) => "1";
        public Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
            => Task.FromResult(OnFetch());
        public NormalizedWorkData Parse(string rawBody) => Parsed ?? new NormalizedWorkData { Source = Source };
    }

    [Fact]
    public async Task A_failing_provider_does_not_abort_enrichment_from_the_others()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-enrich-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection().AddMetaHubInfrastructureSqlite(dbPath).BuildServiceProvider();
        try
        {
            sp.EnsureMetaHubSchemaCreated();
            Guid workId;
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "X" };
                db.Works.Add(work);
                await db.SaveChangesAsync();
                workId = work.Id;
            }

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                // The throwing provider runs first (lower priority number).
                var providers = new IMetadataProvider[]
                {
                    new FakeProvider
                    {
                        Source = ExternalIdSource.Tmdb, Priority = 1,
                        OnFetch = () => throw new HttpRequestException("network down")
                    },
                    new FakeProvider
                    {
                        Source = ExternalIdSource.AniList, Priority = 2,
                        OnFetch = () => "{}",
                        Parsed = new NormalizedWorkData { Source = ExternalIdSource.AniList, Overview = "Survived" }
                    },
                };

                var svc = new EnrichmentService(db, providers,
                    Options.Create(new EnrichmentOptions { WriteMode = EnrichmentWriteMode.Overwrite }),
                    NullLogger<EnrichmentService>.Instance);

                var result = await svc.EnrichAsync(workId, forceRefresh: false, writeMode: null, ct: CancellationToken.None);
                Assert.Contains("AniList", string.Join(",", result.Applied));
            }

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
                var work = await db.Works.FirstAsync(w => w.Id == workId);
                Assert.Equal("Survived", work.Overview);
            }
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
