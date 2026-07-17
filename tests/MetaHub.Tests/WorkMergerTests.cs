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
    public async Task Unknown_status_from_a_higher_priority_provider_does_not_shadow_a_real_one()
    {
        // AniList maps unrecognized statuses to Unknown (non-null). A lower-priority provider's
        // real status (e.g. AniDB's date-derived Finished) must still be applied.
        await using var db = NewDb();
        var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "x" };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var higherUnknown = new NormalizedWorkData { Source = ExternalIdSource.AniList, Status = WorkStatus.Unknown };
        var lowerFinished = new NormalizedWorkData { Source = ExternalIdSource.AniDb, Status = WorkStatus.Finished };

        await new WorkMerger(db).ApplyAsync(work, new[] { higherUnknown, lowerFinished }, EnrichmentWriteMode.Overwrite);
        await db.SaveChangesAsync();

        Assert.Equal(WorkStatus.Finished, (await db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id)).Status);
    }

    [Fact]
    public async Task TitleTranslations_are_added_even_when_the_canonical_title_is_kept()
    {
        // Reproduces the "romaji title shown" bug: manami seeds the romaji CanonicalTitle, then
        // enrichment runs in the default FillMissingOnly mode. The romaji title is (correctly)
        // kept, but the English title must still be captured as a translation so it can be shown.
        await using var db = NewDb();
        var work = new Work
        {
            MediaType = MediaType.Anime,
            CanonicalTitle = "Youkoso Jitsuryoku Shijou Shugi no Kyoushitsu e" // manami romaji seed
        };
        db.Works.Add(work);
        await db.SaveChangesAsync();

        var aniList = new NormalizedWorkData
        {
            Source = ExternalIdSource.AniList,
            CanonicalTitle = "Classroom of the Elite"
        };
        aniList.TitleTranslations["en"] = "Classroom of the Elite";
        aniList.TitleTranslations["ja"] = "ようこそ実力至上主義の教室へ";

        await new WorkMerger(db).ApplyAsync(work, new[] { aniList }, EnrichmentWriteMode.FillMissingOnly);
        await db.SaveChangesAsync();

        var saved = await db.Works.AsNoTracking().SingleAsync(w => w.Id == work.Id);
        Assert.Equal("Youkoso Jitsuryoku Shijou Shugi no Kyoushitsu e", saved.CanonicalTitle); // kept
        Assert.Equal("Classroom of the Elite", saved.TitleTranslations["en"]);                 // captured
        Assert.Equal("ようこそ実力至上主義の教室へ", saved.TitleTranslations["ja"]);
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
