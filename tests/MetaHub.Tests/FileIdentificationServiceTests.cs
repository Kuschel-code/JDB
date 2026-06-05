using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Identification;
using MetaHub.Identification.AniDb;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

public class FileIdentificationServiceTests
{
    private static MetaHubDbContext NewDb() =>
        new(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FakeAniDbClient : IAniDbClient
    {
        private readonly AniDbFileResult? _result;
        public FakeAniDbClient(bool enabled, AniDbFileResult? result = null)
        {
            IsEnabled = enabled;
            _result = result;
        }

        public bool IsEnabled { get; }
        public Task<AniDbFileResult?> LookupFileAsync(long sizeBytes, string ed2kHash, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    [Fact]
    public async Task Records_file_and_hash_when_anidb_disabled()
    {
        await using var db = NewDb();
        var service = new FileIdentificationService(
            db, new FakeAniDbClient(enabled: false), NullLogger<FileIdentificationService>.Instance);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc");
            var result = await service.IdentifyAnimeFileAsync(path);

            Assert.False(result.Identified);
            Assert.Equal("a448017aaf21d8525fc10ae87aa6729d", result.Ed2kHash);
            Assert.Null(result.WorkId);

            var file = await db.MediaFiles.SingleAsync();
            Assert.Equal(result.Ed2kHash, file.Ed2kHash);
            Assert.Equal(IdentificationMethod.None, file.IdentifiedBy);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Links_file_to_work_when_anidb_matches_ingested_anime()
    {
        await using var db = NewDb();
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "Test Anime" };
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "23" });
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var aniDbResult = new AniDbFileResult
        {
            FileId = "100",
            AnimeId = "23",
            EpisodeId = "555",
            EpisodeNumber = "1",
            RawResponse = "100|23|555|..."
        };
        var service = new FileIdentificationService(
            db, new FakeAniDbClient(enabled: true, aniDbResult),
            NullLogger<FileIdentificationService>.Instance);

        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "anime episode bytes");
            var result = await service.IdentifyAnimeFileAsync(path);

            Assert.True(result.Identified);
            Assert.Equal(work.Id, result.WorkId);
            Assert.Equal(IdentificationMethod.Ed2k, result.Method);
            Assert.Equal(0.99, result.Confidence, 3);

            Assert.Equal(1, await db.RawPayloads.CountAsync(p => p.Source == ExternalIdSource.AniDb));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
