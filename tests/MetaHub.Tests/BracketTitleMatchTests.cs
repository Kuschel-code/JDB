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
/// Regression for the "anime film: title correct but no ids/overview/artwork/people" report.
/// The works' canonical titles carry square brackets ("Fate/stay night [Heaven's Feel] I. presage
/// flower") while the library folders use round parentheses. Name resolution must still match
/// (exercising the SQL Replace chain in <see cref="MetaHubBackend.ResolveByNameAsync"/>), otherwise
/// it returns null and the provider writes a title only — no ids, so no enrichment ever follows.
/// </summary>
public class BracketTitleMatchTests
{
    private static (ServiceProvider sp, string dbPath) NewEmbeddedWithHeavensFeel()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"metahub-bracket-{Guid.NewGuid():N}.db");
        var sp = new ServiceCollection()
            .AddMetaHubInfrastructureSqlite(dbPath)
            .AddHttpClient()
            .BuildServiceProvider();
        sp.EnsureMetaHubSchemaCreated();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
        foreach (var (title, year) in new[]
        {
            ("Fate/stay night [Heaven's Feel] I. presage flower", 2017),
            ("Fate/stay night [Heaven's Feel] II. lost butterfly", 2019),
            ("Fate/stay night [Heaven's Feel] III. spring song", 2020),
        })
        {
            db.Works.Add(new Work
            {
                MediaType = MediaType.Anime,
                CanonicalTitle = title,
                SearchTitles = TitleNormalization.BuildSearchTitles(new[] { title }),
                ReleaseYear = year
            });
        }
        db.SaveChanges();
        return (sp, dbPath);
    }

    private static MetaHubBackend Backend(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        () => new PluginConfiguration { UseEmbeddedEngine = true, EnrichOnDemand = false });

    [Theory]
    [InlineData("Fate/stay night (Heaven's Feel) I. presage flower", "Fate/stay night [Heaven's Feel] I. presage flower")]
    [InlineData("Fate/stay night (Heaven's Feel) II. lost butterfly", "Fate/stay night [Heaven's Feel] II. lost butterfly")]
    [InlineData("Fate stay night Heavens Feel III spring song", "Fate/stay night [Heaven's Feel] III. spring song")]
    public async Task Parenthesised_folder_resolves_to_the_correct_bracketed_part(string folder, string expected)
    {
        var (sp, dbPath) = NewEmbeddedWithHeavensFeel();
        try
        {
            var hit = await Backend(sp).ResolveByNameAsync(
                new[] { folder }, null, MediaType.Anime, folder, null, CancellationToken.None);
            Assert.NotNull(hit);
            Assert.Equal(expected, hit!.CanonicalTitle);
        }
        finally
        {
            sp.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
