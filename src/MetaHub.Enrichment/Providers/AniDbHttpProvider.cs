using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Identification.AniDb;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// AniDB HTTP API metadata provider. Fetches anime XML via <see cref="AniDbHttpClient"/>,
/// converts to JSON for <see cref="RawPayload"/> storage (JSONB on PostgreSQL), and parses
/// into <see cref="NormalizedWorkData"/> for the enrichment merge.
/// </summary>
public class AniDbHttpProvider : IMetadataProvider
{
    private readonly AniDbHttpClient _client;

    public AniDbHttpProvider(AniDbHttpClient client) => _client = client;

    public ExternalIdSource Source => ExternalIdSource.AniDb;
    public int Priority => 12;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.AniDb)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalId, out var aid))
            return null;

        var xml = await _client.FetchAnimeXmlAsync(aid, ct);
        if (xml is null)
            return null;

        var anime = AniDbAnimeParser.Parse(xml);
        return JsonSerializer.Serialize(anime);
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };

        AniDbAnime? anime;
        try { anime = JsonSerializer.Deserialize<AniDbAnime>(rawBody); }
        catch (JsonException) { return data; }
        if (anime is null) return data;

        data.CanonicalTitle = anime.MainTitle;
        data.OriginalTitle = anime.OriginalTitle;
        data.Overview = anime.Description;
        data.EpisodeCount = anime.EpisodeCount;
        data.Status = DeriveStatus(anime.StartDate, anime.EndDate);

        if (anime.StartDate is { Length: >= 4 } sd && int.TryParse(sd[..4], out var year))
            data.ReleaseYear = year;

        // Best title per language: official > main > synonym > short (not first-encountered —
        // AniDB does not guarantee element order, and a synonym must not shadow the official name).
        foreach (var group in anime.Titles
                     .Where(t => !string.IsNullOrWhiteSpace(t.Text) && !string.IsNullOrWhiteSpace(t.Lang))
                     .GroupBy(t => t.Lang!))
        {
            data.TitleTranslations[group.Key] = group.MinBy(t => TitleTypeRank(t.Type))!.Text;
        }

        foreach (var tag in anime.Tags)
            data.Genres.Add(tag);

        if (!string.IsNullOrWhiteSpace(anime.PictureUrl))
            data.Images.Add(new NormalizedImage
            {
                Type = ImageType.Poster,
                Url = anime.PictureUrl,
                Source = "anidb",
                Score = 70
            });

        var order = 0;
        foreach (var ch in anime.Characters)
        {
            if (string.IsNullOrWhiteSpace(ch.SeiyuuName))
                continue;
            data.Credits.Add(new NormalizedCredit
            {
                Name = ch.SeiyuuName,
                Role = CreditRole.VoiceActor,
                Character = ch.Name,
                ImageUrl = ch.SeiyuuImageUrl,
                Order = order++
            });
        }

        return data;
    }

    private static int TitleTypeRank(string? type) => type switch
    {
        "official" => 0,
        "main" => 1,
        "synonym" => 2,
        "short" => 3,
        _ => 4
    };

    /// <summary>
    /// Derives the work status from AniDB's air dates (there is no explicit status field).
    /// Deliberately conservative: only date constellations that are unambiguous evidence
    /// produce a value. "Started with no end date" stays null — AniDB omits the end date
    /// for some finished works and for cancelled/on-hiatus ones, so guessing Ongoing there
    /// would override an explicit status from another provider with a wrong one. Partial
    /// dates ("1998", "1998-04") never count as evidence.
    /// </summary>
    private static WorkStatus? DeriveStatus(string? startDate, string? endDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hasStart = DateOnly.TryParseExact(startDate, "yyyy-MM-dd", out var start);
        var hasEnd = DateOnly.TryParseExact(endDate, "yyyy-MM-dd", out var end);

        if (hasStart && start > today)
            return WorkStatus.Announced;

        if (hasEnd && end < today)
            return WorkStatus.Finished;

        if (hasStart && hasEnd) // started, scheduled end still ahead
            return WorkStatus.Ongoing;

        return null;
    }
}
