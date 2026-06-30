using System.Text.Json;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// fanart.tv artwork provider (free, instant API key; inert when the key is empty). Adds posters,
/// backgrounds, banners and — uniquely — clear logos, keyed by ids MetaHub already stores: TVDB for
/// series/anime, TMDB/IMDb for movies, MusicBrainz for music. Contributes only artwork; the
/// language tag on each image lets ImageScorer prefer the viewer's localized poster/logo.
/// </summary>
public class FanArtTvProvider : IMetadataProvider
{
    public const string HttpClientName = "fanart";
    private const string ApiBase = "https://webservice.fanart.tv/v3/";

    private readonly IHttpClientFactory _factory;
    private readonly EnrichmentOptions _options;

    public FanArtTvProvider(IHttpClientFactory factory, IOptions<EnrichmentOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    public ExternalIdSource Source => ExternalIdSource.FanArtTv;

    // Artwork only: images are merged/scored across providers, so the priority barely matters.
    public int Priority => 25;

    public string? GetExternalId(Work work)
    {
        string? Id(ExternalIdSource s) => work.ExternalIds.FirstOrDefault(x => x.Source == s)?.ExternalValue;
        return work.MediaType switch
        {
            MediaType.Movie => Id(ExternalIdSource.TmdbMovie) ?? Id(ExternalIdSource.Tmdb) ?? Id(ExternalIdSource.Imdb),
            MediaType.Music => Id(ExternalIdSource.MusicBrainz),
            _ => Id(ExternalIdSource.Tvdb) // anime / series use the TVDB id
        };
    }

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FanArtApiKey) || string.IsNullOrWhiteSpace(externalId))
            return null;

        var endpoint = work.MediaType switch
        {
            MediaType.Movie => "movies/",
            MediaType.Music => "music/",
            _ => "tv/"
        };
        var url = $"{ApiBase}{endpoint}{Uri.EscapeDataString(externalId)}?api_key={_options.FanArtApiKey}";

        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
    }

    // fanart.tv groups artwork under type-named arrays; map the ones Jellyfin renders. Base scores
    // sit alongside AniList (80) / TMDB (90) so a language-matched fanart image can win, while logos
    // (which no other provider supplies) come through regardless.
    private static readonly (string Key, ImageType Type, double Score)[] Sets =
    {
        ("tvposter", ImageType.Poster, 80), ("movieposter", ImageType.Poster, 80),
        ("showbackground", ImageType.Backdrop, 85), ("moviebackground", ImageType.Backdrop, 85),
        ("hdtvlogo", ImageType.Logo, 72), ("hdmovielogo", ImageType.Logo, 72),
        ("clearlogo", ImageType.Logo, 66), ("movielogo", ImageType.Logo, 66),
        ("tvbanner", ImageType.Banner, 55), ("moviebanner", ImageType.Banner, 55),
        ("tvthumb", ImageType.Thumb, 55), ("moviethumb", ImageType.Thumb, 55),
    };

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return data;

        foreach (var (key, type, score) in Sets)
        {
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var img in arr.EnumerateArray())
            {
                if (GetString(img, "url") is not { } url)
                    continue;
                var lang = GetString(img, "lang");
                var likes = int.TryParse(GetString(img, "likes"), out var l) ? l : 0;
                data.Images.Add(new NormalizedImage
                {
                    Type = type,
                    Url = url,
                    // fanart uses "" or "00" for language-neutral art.
                    Lang = lang is null or "" or "00" ? null : lang,
                    Source = "fanart",
                    Score = score + Math.Min(10, likes)
                });
            }
        }

        return data;
    }

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
