using Microsoft.EntityFrameworkCore;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Infrastructure;

namespace MetaHub.Enrichment;

/// <summary>
/// Merges normalized data from several providers into a canonical <see cref="Work"/> using
/// per-field source priority: the inputs are applied in priority order and the first
/// non-empty value wins. Genres are unioned; images are collected and de-duplicated by URL.
/// </summary>
public class WorkMerger
{
    private readonly MetaHubDbContext _db;

    public WorkMerger(MetaHubDbContext db) => _db = db;

    /// <summary>
    /// Applies <paramref name="ordered"/> (already sorted most-authoritative first) to
    /// <paramref name="work"/>. In <see cref="EnrichmentWriteMode.FillMissingOnly"/> existing
    /// values are preserved; in <see cref="EnrichmentWriteMode.Overwrite"/> they are replaced.
    /// New genres and images are always added (additive), never removed. Caller saves.
    /// </summary>
    public async Task ApplyAsync(
        Work work,
        IReadOnlyList<NormalizedWorkData> ordered,
        EnrichmentWriteMode writeMode = EnrichmentWriteMode.Overwrite,
        string? preferredLanguage = null,
        CancellationToken ct = default)
    {
        if (ordered.Count == 0)
            return;

        // Scalars: first non-null in priority order, subject to the write mode.
        work.CanonicalTitle = Pick(writeMode, work.CanonicalTitle, FirstNonEmpty(ordered, d => d.CanonicalTitle),
            isEmpty: string.IsNullOrWhiteSpace) ?? work.CanonicalTitle;
        work.OriginalTitle = Pick(writeMode, work.OriginalTitle, FirstNonEmpty(ordered, d => d.OriginalTitle),
            isEmpty: string.IsNullOrWhiteSpace);
        work.Overview = Pick(writeMode, work.Overview, FirstNonEmpty(ordered, d => d.Overview),
            isEmpty: string.IsNullOrWhiteSpace);
        work.ReleaseYear = Pick(writeMode, work.ReleaseYear, First(ordered, d => d.ReleaseYear),
            isEmpty: v => v is null);

        var status = First(ordered, d => d.Status);
        if (status is { } s && s != WorkStatus.Unknown &&
            (writeMode == EnrichmentWriteMode.Overwrite || work.Status == WorkStatus.Unknown))
            work.Status = s;

        // Overview translations: union, higher priority wins on key clash (unless fill-only).
        foreach (var data in ordered.Reverse()) // apply low→high so high overwrites
            foreach (var (lang, text) in data.OverviewTranslations)
                if (writeMode == EnrichmentWriteMode.Overwrite || !work.OverviewTranslations.ContainsKey(lang))
                    work.OverviewTranslations[lang] = text;

        work.UpdatedAt = DateTimeOffset.UtcNow;

        await ApplyDetailAsync(work, ordered, writeMode, ct);
        await ApplyGenresAsync(work, ordered, ct);
        ApplyImages(work, ordered, preferredLanguage);
    }

    private async Task ApplyDetailAsync(
        Work work, IReadOnlyList<NormalizedWorkData> ordered, EnrichmentWriteMode writeMode, CancellationToken ct)
    {
        switch (work.MediaType)
        {
            case MediaType.Series or MediaType.Anime:
            {
                var episodeCount = First(ordered, d => d.EpisodeCount);
                var seasonCount = First(ordered, d => d.SeasonCount);
                var network = FirstNonEmpty(ordered, d => d.Network);
                if (episodeCount is null && seasonCount is null && network is null)
                    break;

                var detail = await GetOrAddDetailAsync(
                    work.SeriesDetail, _db.SeriesDetails, s => s.WorkId == work.Id,
                    () => new SeriesDetail { WorkId = work.Id }, ct);
                detail.EpisodeCount = Pick(writeMode, detail.EpisodeCount, episodeCount, v => v is null);
                detail.SeasonCount = Pick(writeMode, detail.SeasonCount, seasonCount, v => v is null);
                detail.Network = Pick(writeMode, detail.Network, network, string.IsNullOrWhiteSpace);
                work.SeriesDetail = detail;
                break;
            }
            case MediaType.Music:
            {
                var trackCount = First(ordered, d => d.TrackCount);
                var albumType = FirstNonEmpty(ordered, d => d.AlbumType);
                var label = FirstNonEmpty(ordered, d => d.Label);
                if (trackCount is null && albumType is null && label is null)
                    break;

                var detail = await GetOrAddDetailAsync(
                    work.MusicDetail, _db.MusicDetails, m => m.WorkId == work.Id,
                    () => new MusicDetail { WorkId = work.Id }, ct);
                detail.TrackCount = Pick(writeMode, detail.TrackCount, trackCount, v => v is null);
                detail.AlbumType = Pick(writeMode, detail.AlbumType, albumType, string.IsNullOrWhiteSpace);
                detail.Label = Pick(writeMode, detail.Label, label, string.IsNullOrWhiteSpace);
                work.MusicDetail = detail;
                break;
            }
            case MediaType.Book:
            {
                var isbn = FirstNonEmpty(ordered, d => d.Isbn13);
                var pages = First(ordered, d => d.PageCount);
                var publisher = FirstNonEmpty(ordered, d => d.Publisher);
                var seriesName = FirstNonEmpty(ordered, d => d.SeriesName);
                var seriesIndex = First(ordered, d => d.SeriesIndex);
                if (isbn is null && pages is null && publisher is null && seriesName is null)
                    break;

                var detail = await GetOrAddDetailAsync(
                    work.BookDetail, _db.BookDetails, bk => bk.WorkId == work.Id,
                    () => new BookDetail { WorkId = work.Id }, ct);
                detail.Isbn13 = Pick(writeMode, detail.Isbn13, isbn, string.IsNullOrWhiteSpace);
                detail.PageCount = Pick(writeMode, detail.PageCount, pages, v => v is null);
                detail.Publisher = Pick(writeMode, detail.Publisher, publisher, string.IsNullOrWhiteSpace);
                detail.SeriesName = Pick(writeMode, detail.SeriesName, seriesName, string.IsNullOrWhiteSpace);
                detail.SeriesIndex = Pick(writeMode, detail.SeriesIndex, seriesIndex, v => v is null);
                work.BookDetail = detail;
                break;
            }
        }
    }

    /// <summary>
    /// Returns the already-attached detail, an existing one from the store, or a freshly
    /// created one that is explicitly added (so EF tracks it as Added, not Modified).
    /// </summary>
    private async Task<TDetail> GetOrAddDetailAsync<TDetail>(
        TDetail? attached,
        DbSet<TDetail> set,
        System.Linq.Expressions.Expression<Func<TDetail, bool>> predicate,
        Func<TDetail> create,
        CancellationToken ct)
        where TDetail : class
    {
        if (attached is not null)
            return attached;

        var existing = await set.FirstOrDefaultAsync(predicate, ct);
        if (existing is not null)
            return existing;

        var created = create();
        set.Add(created);
        return created;
    }

    private async Task ApplyGenresAsync(Work work, IReadOnlyList<NormalizedWorkData> ordered, CancellationToken ct)
    {
        var names = ordered.SelectMany(d => d.Genres)
            .Select(g => g.Trim())
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return;

        var existingLinks = await _db.WorkGenres
            .Where(wg => wg.WorkId == work.Id)
            .Select(wg => wg.GenreId)
            .ToListAsync(ct);
        var linkedIds = existingLinks.ToHashSet();

        foreach (var name in names)
        {
            var genre = await _db.Genres.FirstOrDefaultAsync(g => g.Name == name, ct);
            if (genre is null)
            {
                genre = new Genre { Name = name };
                _db.Genres.Add(genre);
            }

            if (!linkedIds.Contains(genre.Id))
            {
                _db.WorkGenres.Add(new WorkGenre { WorkId = work.Id, Genre = genre });
                linkedIds.Add(genre.Id);
            }
        }
    }

    private void ApplyImages(Work work, IReadOnlyList<NormalizedWorkData> ordered, string? preferredLanguage)
    {
        var existingUrls = work.Images.Select(i => i.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var image in ordered.SelectMany(d => d.Images))
        {
            if (string.IsNullOrWhiteSpace(image.Url) || !existingUrls.Add(image.Url))
                continue;

            var entity = new Image
            {
                WorkId = work.Id,
                Type = image.Type,
                Url = image.Url,
                Lang = image.Lang,
                Width = image.Width,
                Height = image.Height,
                Source = image.Source,
                Score = ImageScorer.Score(image, preferredLanguage)
            };
            // Add via the set so EF tracks it as Added even though its client-generated key is set.
            _db.Images.Add(entity);
            work.Images.Add(entity);
        }
    }

    // Chooses between an existing and an incoming value according to the write mode.
    private static string? Pick(EnrichmentWriteMode mode, string? existing, string? incoming, Func<string?, bool> isEmpty)
    {
        if (incoming is null || isEmpty(incoming)) return existing;
        if (mode == EnrichmentWriteMode.Overwrite) return incoming;
        return isEmpty(existing) ? incoming : existing;
    }

    private static T? Pick<T>(EnrichmentWriteMode mode, T? existing, T? incoming, Func<T?, bool> isEmpty)
        where T : struct
    {
        if (incoming is null) return existing;
        if (mode == EnrichmentWriteMode.Overwrite) return incoming;
        return isEmpty(existing) ? incoming : existing;
    }

    private static string? FirstNonEmpty(IReadOnlyList<NormalizedWorkData> ordered, Func<NormalizedWorkData, string?> select)
        => ordered.Select(select).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static T? First<T>(IReadOnlyList<NormalizedWorkData> ordered, Func<NormalizedWorkData, T?> select)
        where T : struct
        => ordered.Select(select).FirstOrDefault(v => v.HasValue);
}
