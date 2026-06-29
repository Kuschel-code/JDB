using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Infrastructure;

namespace MetaHub.Jellyfin.Api;

/// <summary>
/// Backend that serves the plugin either from the embedded engine (local SQLite + in-process
/// enrichment) or from a remote MetaHub API, chosen per call from the plugin configuration.
/// A fresh DI scope is created per operation so this works regardless of provider lifetime.
/// </summary>
public class MetaHubBackend : IMetaHubBackend
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<Configuration.PluginConfiguration> _configAccessor;

    public MetaHubBackend(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        Func<Configuration.PluginConfiguration> configAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configAccessor = configAccessor;
    }

    private Configuration.PluginConfiguration Config => _configAccessor();

    public async Task<WorkDto?> ResolveAsync(
        IReadOnlyDictionary<string, string> providerIds, string? lang, CancellationToken ct)
    {
        if (!Config.UseEmbeddedEngine)
        {
            var client = new MetaHubApiClient(_httpClientFactory);
            return await MetaHubMapping.ResolveAsync(client, providerIds, lang, ct).ConfigureAwait(false);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        Work? work;
        try
        {
            work = await FindWorkIdAsync(db, providerIds, ct).ConfigureAwait(false) is { } wid
                ? await LoadWorkAsync(db, wid, ct).ConfigureAwait(false)
                : null;
        }
        catch (System.Data.Common.DbException)
        {
            // Transient DB error (e.g. SQLITE_BUSY under concurrent scan+enrich) — fail soft so the
            // provider's GetMetadata returns empty rather than throwing and being dropped.
            return null;
        }

        if (work is null)
            return null;

        // On-demand enrichment when the work has not been enriched yet (best-effort).
        work = await EnrichOnDemandAsync(scope, db, work, ct).ConfigureAwait(false);

        return ToDto(work, lang);
    }

    /// <summary>
    /// Best-effort on-demand enrichment: when enabled and the work still has no overview, fetch
    /// and merge provider data, then reload. Failures are swallowed (serve whatever is stored).
    /// Shared by id- and name-based resolution so a folder-name match fills overview/artwork/people
    /// just like an id match — otherwise a name-matched work is served with only its ingest data
    /// (title + a lone manami poster, no overview/cast).
    /// </summary>
    private async Task<Work> EnrichOnDemandAsync(IServiceScope scope, MetaHubDbContext db, Work work, CancellationToken ct)
    {
        if (!Config.EnrichOnDemand || !string.IsNullOrWhiteSpace(work.Overview))
            return work;

        try
        {
            var enrichment = scope.ServiceProvider.GetRequiredService<EnrichmentService>();
            await enrichment.EnrichAsync(work.Id, false, null, ct).ConfigureAwait(false);
            return await LoadWorkAsync(db, work.Id, ct).ConfigureAwait(false) ?? work;
        }
        catch
        {
            return work; // best-effort: serve whatever is already stored
        }
    }

    public async Task<IReadOnlyList<ImageDto>> GetImagesAsync(Guid workId, CancellationToken ct)
    {
        if (!Config.UseEmbeddedEngine)
        {
            var client = new MetaHubApiClient(_httpClientFactory);
            return await client.GetImagesAsync(workId, ct).ConfigureAwait(false);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        try
        {
            return await db.Images
                .Where(i => i.WorkId == workId)
                .OrderByDescending(i => i.Score)
                .Select(i => new ImageDto
                {
                    Type = i.Type.ToString(),
                    Url = i.Url,
                    Lang = i.Lang,
                    Width = i.Width,
                    Height = i.Height,
                    Source = i.Source,
                    Score = i.Score
                })
                .ToListAsync(ct).ConfigureAwait(false);
        }
        catch (System.Data.Common.DbException)
        {
            return Array.Empty<ImageDto>(); // transient DB error — fail soft (no artwork this pass)
        }
    }

    public async Task<WorkDto?> ResolveByNameAsync(
        IEnumerable<string> nameCandidates, int? year, MediaType? preferredType,
        string? folderName, string? lang, CancellationToken ct)
    {
        // The folder name is also a search candidate (tried first): it carries the sequel suffix
        // a pre-set item name may be missing, and is needed to actually fetch the sequel work.
        var names = new[] { folderName }
            .Concat(nameCandidates)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return null;

        if (!Config.UseEmbeddedEngine)
        {
            // Remote mode: title lookup is not exposed as a dedicated endpoint yet.
            return null;
        }

        // The folder name is the authoritative source for the season/sequel distinction: a user
        // organizes "… Reawakened" vs the base series by folder, while Jellyfin's pre-set item
        // name may already be the wrong (base) name. When a folder name is given, its sequel key
        // constrains every match; otherwise fall back to each candidate's own key.
        var folderKey = string.IsNullOrWhiteSpace(folderName) ? null : SequelKey(folderName);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        try
        {
        foreach (var name in names)
        {
            // ASCII-only fold to mirror SQLite's lower() (see TitleNormalization.AsciiLower): full
            // Unicode ToLowerInvariant would never compare equal to the EF-translated lower() for a
            // title with a non-ASCII uppercase letter (Ō, Ä, Cyrillic…), silently failing the match.
            var lowered = MetaHub.Domain.TitleNormalization.AsciiLower(name);
            var norm = NormTitle(name);
            // Segment needle for the per-work searchable title set (primary title + synonyms).
            var needle = MetaHub.Domain.TitleNormalization.SearchNeedle(name);

            // Restrict to the library's media type when known. Every manami anime work — including
            // films and OVAs — is stored as MediaType.Anime, so a library typed Movie/Series (e.g.
            // "Filme") must still accept Anime works; an Anime library stays strict so it can prefer
            // the anime "22/7" over the US series "227".
            var query = db.Works.AsQueryable();
            if (preferredType is { } pt)
                query = pt is MediaType.Movie or MediaType.Series
                    ? query.Where(w => w.MediaType == pt || w.MediaType == MediaType.Anime)
                    : query.Where(w => w.MediaType == pt);

            // Exact (case-insensitive) OR punctuation-insensitive match so a folder "227" finds the
            // anime "22/7"; plus a match against the work's full title set (SearchTitles) so a folder
            // named after a synonym — e.g. the English "Even If the World Ends Tomorrow" — finds the
            // romaji entry "Ashita Sekai ga Owaru to Shitemo". The Replace chain and Contains are
            // translated to SQLite replace()/instr() by EF Core.
            var matches = await query
                .Where(w =>
                    w.CanonicalTitle.ToLower() == lowered
                    || (w.OriginalTitle != null && w.OriginalTitle.ToLower() == lowered)
                    || w.CanonicalTitle.ToLower()
                        .Replace(" ", "").Replace("/", "").Replace("-", "").Replace(":", "")
                        .Replace(".", "").Replace(",", "").Replace("'", "").Replace("!", "")
                        .Replace("?", "").Replace("_", "")
                        .Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "")
                        .Replace("{", "").Replace("}", "") == norm
                    || (w.OriginalTitle != null && w.OriginalTitle.ToLower()
                        .Replace(" ", "").Replace("/", "").Replace("-", "").Replace(":", "")
                        .Replace(".", "").Replace(",", "").Replace("'", "").Replace("!", "")
                        .Replace("?", "").Replace("_", "")
                        .Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "")
                        .Replace("{", "").Replace("}", "") == norm)
                    || (w.SearchTitles != "" && w.SearchTitles.Contains(needle)))
                .Select(w => new { w.Id, w.ReleaseYear, w.CanonicalTitle, w.OriginalTitle })
                .Take(8)
                .ToListAsync(ct).ConfigureAwait(false);

            if (matches.Count == 0)
                continue;

            // Sequel guard: never let a folder with a season/sequel marker fall onto the base
            // series (or vice versa). "Saiki K. Reawakened" must not resolve to "Saiki K.".
            // The folder's key wins over the (possibly base-named) candidate.
            var wantedKey = folderKey ?? SequelKey(name);
            matches = matches
                .Where(m => SequelKey(m.CanonicalTitle) == wantedKey
                            || (m.OriginalTitle != null && SequelKey(m.OriginalTitle) == wantedKey))
                .ToList();
            if (matches.Count == 0)
                continue;

            // Prefer an exact title hit over a punctuation-only (fuzzy) hit.
            var exact = matches
                .Where(m => string.Equals(m.CanonicalTitle, name, StringComparison.OrdinalIgnoreCase)
                            || (m.OriginalTitle != null
                                && string.Equals(m.OriginalTitle, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var pool = exact.Count > 0 ? exact : matches;

            // Disambiguate by year when several works remain (remakes etc.).
            var pick = pool.Count == 1
                ? pool[0]
                : year is { } y
                    ? pool.FirstOrDefault(m => m.ReleaseYear == y)
                    : null;
            if (pick is null)
                continue;

            var work = await LoadWorkAsync(db, pick.Id, ct).ConfigureAwait(false);
            if (work is not null)
            {
                // Same on-demand enrichment as the id path, so a folder-name match also gets
                // overview/artwork/people instead of just the manami title.
                work = await EnrichOnDemandAsync(scope, db, work, ct).ConfigureAwait(false);
                return ToDto(work, lang);
            }
        }
        }
        catch (System.Data.Common.DbException)
        {
            return null; // transient DB error — fail soft (name fallback is best-effort)
        }

        return null;
    }

    /// <summary>Lowercase a title and strip common separators so "22/7" and "227" compare equal.
    /// Must mirror the SQL lower()+replace() chain in <see cref="ResolveByNameAsync"/> (ASCII-only
    /// fold); shared with the ingest so a work's stored <see cref="Work.SearchTitles"/> normalize
    /// the same way.</summary>
    private static string NormTitle(string? title) => MetaHub.Domain.TitleNormalization.Normalize(title);

    /// <summary>
    /// A stable "season/sequel signature" for a title: the set of markers that distinguish a
    /// sequel/part/season/movie from the base entry. Two titles with different keys are
    /// different works (e.g. base "" vs "reawakened", or season "2"). Used to stop near-identical
    /// titles from cross-matching during name resolution.
    /// </summary>
    internal static string SequelKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;
        var t = title.ToLowerInvariant();
        var tokens = new SortedSet<string>(StringComparer.Ordinal);

        // "season 2", "staffel 2", "part 2", "cour 2", "2nd season"
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(t, @"\b(?:season|staffel|part|cour)\s*0*([2-9])\b"))
            tokens.Add(m.Groups[1].Value);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(t, @"\b0*([2-9])(?:st|nd|rd|th)\b"))
            tokens.Add(m.Groups[1].Value);

        // Roman numerals II–IV as standalone words.
        foreach (var (roman, num) in new[] { ("ii", "2"), ("iii", "3"), ("iv", "4") })
            if (System.Text.RegularExpressions.Regex.IsMatch(t, $@"\b{roman}\b"))
                tokens.Add(num);

        // Content markers that denote a genuinely different ENTRY (a sequel/continuation) and must
        // match. Format-only words (movie/film/ova/ona/special/recap) are deliberately NOT here:
        // they describe the same work's release form and libraries routinely omit them, so a manami
        // canonical "Fate/stay night Movie: …" must still match a folder "Fate/stay night …".
        foreach (var kw in new[] { "reawaken", "final" })
            if (System.Text.RegularExpressions.Regex.IsMatch(t, $@"\b{kw}"))
                tokens.Add(kw);

        return string.Join("|", tokens);
    }

    public async Task<EpisodeDto?> GetEpisodeAsync(
        IReadOnlyDictionary<string, string> seriesProviderIds, int? seasonNumber, int? episodeNumber,
        CancellationToken ct)
    {
        if (episodeNumber is null)
            return null;

        if (!Config.UseEmbeddedEngine)
        {
            var client = new MetaHubApiClient(_httpClientFactory);
            var series = await MetaHubMapping.ResolveAsync(client, seriesProviderIds, null, ct).ConfigureAwait(false);
            if (series is null)
                return null;

            var episodes = await client.GetEpisodesAsync(series.Id, ct).ConfigureAwait(false);
            var match = episodes.FirstOrDefault(e =>
                            e.SeasonNumber == (seasonNumber ?? 1) && e.EpisodeNumber == episodeNumber)
                        ?? episodes.FirstOrDefault(e => e.AbsoluteNumber == episodeNumber);
            if (match is not null)
                match.SeriesMediaType = series.MediaType;
            return match;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();

        try
        {
        var workId = await FindWorkIdAsync(db, seriesProviderIds, ct).ConfigureAwait(false);
        if (workId is null)
            return null;

        var mediaType = await db.Works.Where(w => w.Id == workId)
            .Select(w => w.MediaType.ToString())
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        var season = seasonNumber ?? 1;
        var episode = await db.Episodes.AsNoTracking()
                          .FirstOrDefaultAsync(e => e.SeriesWorkId == workId
                                                    && e.SeasonNumber == season
                                                    && e.EpisodeNumber == episodeNumber, ct)
                          .ConfigureAwait(false)
                      // Anime libraries often use absolute numbering.
                      ?? await db.Episodes.AsNoTracking()
                          .FirstOrDefaultAsync(e => e.SeriesWorkId == workId
                                                    && e.AbsoluteNumber == episodeNumber, ct)
                          .ConfigureAwait(false);

        if (episode is null)
            return null;

        return new EpisodeDto
        {
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            AbsoluteNumber = episode.AbsoluteNumber,
            Title = episode.Title,
            Overview = episode.Overview,
            AirDate = episode.AirDate,
            SeriesMediaType = mediaType
        };
        }
        catch (System.Data.Common.DbException)
        {
            return null; // transient DB error — fail soft
        }
    }

    /// <summary>Finds the work referenced by any of the item's provider ids, in preference order.</summary>
    private static async Task<Guid?> FindWorkIdAsync(
        MetaHubDbContext db, IReadOnlyDictionary<string, string> providerIds, CancellationToken ct)
    {
        foreach (var (source, id) in ProviderIdMapper.Candidates(providerIds))
        {
            if (!TryMapSource(source, out var src))
                continue;

            // Jellyfin uses one bare "Tmdb" key for both tv and movies, but their id spaces overlap,
            // so a movie's id is stored under TmdbMovie (see Fribb ingest). Match either for TMDB.
            var alsoTmdbMovie = src == ExternalIdSource.Tmdb;
            var workId = await db.ExternalIds
                .Where(x => x.ExternalValue == id
                            && (x.Source == src || (alsoTmdbMovie && x.Source == ExternalIdSource.TmdbMovie)))
                .Select(x => (Guid?)x.WorkId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (workId is { } wid)
                return wid;
        }

        return null;
    }

    private static Task<Work?> LoadWorkAsync(MetaHubDbContext db, Guid id, CancellationToken ct)
        => db.Works
            .Include(w => w.ExternalIds)
            .Include(w => w.Credits).ThenInclude(c => c.Person)
            .Include(w => w.WorkGenres).ThenInclude(g => g.Genre)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    /// <summary>
    /// Chooses the title to display: the viewer's preferred language, then English, then the
    /// canonical title. This keeps an anime from showing its romaji <c>CanonicalTitle</c> (manami's
    /// primary title) when an English/localized title exists — mirroring overview localization.
    /// </summary>
    public static string PickTitle(
        string canonicalTitle, IReadOnlyDictionary<string, string> titleTranslations, string? preferredLanguage)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguage)
            && titleTranslations.TryGetValue(preferredLanguage, out var preferred)
            && !string.IsNullOrWhiteSpace(preferred))
            return preferred;

        if (titleTranslations.TryGetValue("en", out var english) && !string.IsNullOrWhiteSpace(english))
            return english;

        return canonicalTitle;
    }

    /// <summary>
    /// Title selection honoring the user's <see cref="Configuration.PluginConfiguration.TitlePreference"/>:
    /// <list type="bullet">
    /// <item><c>Romaji</c> — the canonical (manami) title as-is.</item>
    /// <item><c>English</c> — the English title, else canonical.</item>
    /// <item><c>Original</c> — the native (e.g. Japanese) title, else canonical.</item>
    /// <item><c>Localized</c> (default) — preferred language → English → canonical.</item>
    /// </list>
    /// </summary>
    public static string PickTitle(
        string canonicalTitle, string? originalTitle,
        IReadOnlyDictionary<string, string> titleTranslations, string? preferredLanguage, string? preference)
    {
        switch ((preference ?? "Localized").Trim().ToLowerInvariant())
        {
            case "romaji" or "canonical":
                return canonicalTitle;
            case "english" or "en":
                return Translation(titleTranslations, "en") ?? canonicalTitle;
            case "original" or "native" or "japanese" or "ja":
                return Translation(titleTranslations, "ja")
                       ?? (string.IsNullOrWhiteSpace(originalTitle) ? canonicalTitle : originalTitle!);
            default: // "localized" / unknown → existing behavior
                return PickTitle(canonicalTitle, titleTranslations, preferredLanguage);
        }
    }

    private static string? Translation(IReadOnlyDictionary<string, string> translations, string key)
        => translations.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private WorkDto ToDto(Work work, string? lang)
    {
        var overview = work.Overview;
        if (!string.IsNullOrWhiteSpace(lang) &&
            work.OverviewTranslations.TryGetValue(lang, out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
            overview = localized;

        return new WorkDto
        {
            Id = work.Id,
            MediaType = work.MediaType.ToString(),
            CanonicalTitle = PickTitle(
                work.CanonicalTitle, work.OriginalTitle, work.TitleTranslations, lang, Config.TitlePreference),
            OriginalTitle = work.OriginalTitle,
            ReleaseYear = work.ReleaseYear,
            Overview = overview,
            Status = work.Status.ToString(),
            Genres = work.WorkGenres
                .Where(g => g.Genre != null && !string.IsNullOrWhiteSpace(g.Genre.Name))
                .Select(g => g.Genre!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ExternalIds = work.ExternalIds
                .Select(x => new ExternalIdDto { Source = x.Source.ToString(), Value = x.ExternalValue })
                .ToList(),
            People = work.Credits
                .OrderBy(c => c.Order)
                .Where(c => c.Person is not null)
                .Select(c => new PersonDto
                {
                    Name = c.Person!.Name,
                    Role = c.Role.ToString(),
                    Character = c.Character,
                    ImageUrl = c.Person.ImageUrl,
                    Order = c.Order
                })
                .ToList()
        };
    }

    private static bool TryMapSource(string metaHubSource, out ExternalIdSource source)
    {
        source = metaHubSource switch
        {
            "anidb" => ExternalIdSource.AniDb,
            "tmdb" => ExternalIdSource.Tmdb,
            "tvdb" => ExternalIdSource.Tvdb,
            "imdb" => ExternalIdSource.Imdb,
            "anilist" => ExternalIdSource.AniList,
            "mal" => ExternalIdSource.Mal,
            "kitsu" => ExternalIdSource.Kitsu,
            "animeplanet" => ExternalIdSource.AnimePlanet,
            "musicbrainz" => ExternalIdSource.MusicBrainz,
            "isbn" => ExternalIdSource.Isbn,
            _ => ExternalIdSource.Unknown
        };
        return source != ExternalIdSource.Unknown;
    }
}
