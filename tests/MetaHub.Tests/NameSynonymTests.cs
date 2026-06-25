using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Regression for the "anime film not recognized although the name is correct" report: the item
/// folder is named after the English synonym ("Even If the World Ends Tomorrow") while manami's
/// primary title is the romaji ("Ashita Sekai ga Owaru to Shitemo"). Name matching must find the
/// work through its stored synonym set (<see cref="Work.SearchTitles"/>).
/// </summary>
public class NameSynonymTests
{
    private static (ServiceProvider sp, string dbPath) NewEmbedded(out Work seeded)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-syn-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection()
            .AddMetaHubInfrastructureSqlite(dbPath)
            .AddHttpClient()
            .BuildServiceProvider();
        sp.EnsureMetaHubSchemaCreated();

        var work = new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "Ashita Sekai ga Owaru to Shitemo",
            OriginalTitle = "明日世界が終わるとしても",
            ReleaseYear = 2019,
            // Built exactly as the ingest does (primary + synonyms).
            SearchTitles = TitleNormalization.BuildSearchTitles(new[]
            {
                "Ashita Sekai ga Owaru to Shitemo",
                "明日世界が終わるとしても",
                "Even If the World Ends Tomorrow",
                "If It's for My Daughter, I'd Even Defeat a Demon Lord" // unrelated decoy synonym
            })
        };

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
            db.Works.Add(work);
            db.SaveChanges();
        }

        seeded = work;
        return (sp, dbPath);
    }

    private static MetaHubBackend Backend(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

    [Fact]
    public async Task Folder_named_after_english_synonym_resolves_to_romaji_work()
    {
        var (sp, dbPath) = NewEmbedded(out _);
        try
        {
            var backend = Backend(sp);

            // The folder name (English synonym) is the candidate Jellyfin hands us.
            var byName = await backend.ResolveByNameAsync(
                new[] { "Even If the World Ends Tomorrow" }, 2019, MediaType.Anime, null, null, CancellationToken.None);
            Assert.NotNull(byName);
            Assert.Equal("Ashita Sekai ga Owaru to Shitemo", byName!.CanonicalTitle);

            // Same via the folderName argument (path-authoritative entry point).
            var byFolder = await backend.ResolveByNameAsync(
                new[] { "Even If the World Ends Tomorrow" }, null, MediaType.Anime,
                "Even If the World Ends Tomorrow", null, CancellationToken.None);
            Assert.NotNull(byFolder);
            Assert.Equal("Ashita Sekai ga Owaru to Shitemo", byFolder!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task A_decoy_synonym_does_not_let_unrelated_name_match()
    {
        var (sp, dbPath) = NewEmbedded(out _);
        try
        {
            // A name that is no title/synonym of the work must not resolve to it.
            var result = await Backend(sp).ResolveByNameAsync(
                new[] { "Some Completely Different Show" }, null, MediaType.Anime, null, null, CancellationToken.None);
            Assert.Null(result);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void BuildSearchTitles_normalizes_and_delimits()
    {
        var blob = TitleNormalization.BuildSearchTitles(new[] { "Saiki K.", "22/7", "Saiki K." });
        // Deduped, normalized, pipe-wrapped; the needle for a known title is contained.
        Assert.Contains(TitleNormalization.SearchNeedle("Saiki K"), blob);
        Assert.Contains(TitleNormalization.SearchNeedle("227"), blob);
        Assert.Equal("|saikik|227|", blob);
    }
}
