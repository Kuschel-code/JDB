using Microsoft.EntityFrameworkCore;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Infrastructure;
using Xunit;

namespace MetaHub.Tests;

public class WorkMergerTests
{
    private static MetaHubDbContext NewDb() =>
        new(new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Merges_by_priority_and_unions_genres_and_images()
    {
        await using var db = NewDb();
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "placeholder" };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        // Highest priority first (AniList), then Jikan.
        var aniList = new NormalizedWorkData
        {
            Source = ExternalIdSource.AniList,
            CanonicalTitle = "AniList Title",
            Overview = "AniList overview",
            ReleaseYear = 1998,
            Status = WorkStatus.Finished,
            EpisodeCount = 26
        };
        aniList.Genres.Add("Action");
        aniList.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = "https://img/a.jpg", Score = 80 });

        var jikan = new NormalizedWorkData
        {
            Source = ExternalIdSource.Mal,
            CanonicalTitle = "Jikan Title",   // should NOT win
            Overview = "Jikan overview",      // should NOT win
            ReleaseYear = 1999,
            EpisodeCount = 26
        };
        jikan.Genres.Add("Sci-Fi");
        jikan.Genres.Add("Action");           // duplicate, unioned
        jikan.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = "https://img/a.jpg", Score = 60 }); // dup URL
        jikan.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = "https://img/b.jpg", Score = 60 });

        await new WorkMerger(db).ApplyAsync(work, new[] { aniList, jikan }, EnrichmentWriteMode.Overwrite);
        await db.SaveChangesAsync();

        var saved = await db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id);
        Assert.Equal("AniList Title", saved.CanonicalTitle);
        Assert.Equal("AniList overview", saved.Overview);
        Assert.Equal(1998, saved.ReleaseYear);
        Assert.Equal(WorkStatus.Finished, saved.Status);

        var seriesDetail = await db.SeriesDetails.AsNoTracking().SingleAsync(s => s.WorkId == work.Id);
        Assert.Equal(26, seriesDetail.EpisodeCount);

        // Counts queried separately (InMemory inflates collection Includes via cartesian joins).
        Assert.Equal(2, await db.WorkGenres.CountAsync(wg => wg.WorkId == work.Id)); // Action + Sci-Fi
        Assert.Equal(2, await db.Images.CountAsync(i => i.WorkId == work.Id));       // a.jpg + b.jpg (deduped)
    }

    [Fact]
    public async Task FillMissingOnly_preserves_existing_scalars_but_adds_new_data()
    {
        await using var db = NewDb();
        var work = new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "Existing Title",
            Overview = "Existing overview"
            // ReleaseYear intentionally null (a gap to be filled)
        };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var incoming = new NormalizedWorkData
        {
            Source = ExternalIdSource.AniList,
            CanonicalTitle = "New Title",     // must NOT overwrite
            Overview = "New overview",        // must NOT overwrite
            ReleaseYear = 1998                // gap -> should be filled
        };
        incoming.Genres.Add("Action");        // additive regardless of mode

        await new WorkMerger(db).ApplyAsync(work, new[] { incoming }, EnrichmentWriteMode.FillMissingOnly);
        await db.SaveChangesAsync();

        var saved = await db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id);
        Assert.Equal("Existing Title", saved.CanonicalTitle);
        Assert.Equal("Existing overview", saved.Overview);
        Assert.Equal(1998, saved.ReleaseYear);
        Assert.Equal(1, await db.WorkGenres.CountAsync(wg => wg.WorkId == work.Id));
    }
}
