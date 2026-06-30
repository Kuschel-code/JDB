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

        if (anime.StartDate is { Length: >= 4 } sd && int.TryParse(sd[..4], out var year))
            data.ReleaseYear = year;

        foreach (var t in anime.Titles)
        {
            if (string.IsNullOrWhiteSpace(t.Text) || string.IsNullOrWhiteSpace(t.Lang))
                continue;
            if (!data.TitleTranslations.ContainsKey(t.Lang))
                data.TitleTranslations[t.Lang] = t.Text;
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
}
