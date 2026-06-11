using System.Text.Json;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// TMDB provider for movies and series (and anime, complementing AniList). Needs a free API
/// key; when none is configured the provider is inert. The movie vs. tv endpoint is chosen
/// from the work's media type.
/// </summary>
public class TmdbProvider : IMetadataProvider
{
    public const string HttpClientName = "tmdb";
    private const string ImageBase = "https://image.tmdb.org/t/p/original";

    private readonly IHttpClientFactory _factory;
    private readonly EnrichmentOptions _options;

    public TmdbProvider(IHttpClientFactory factory, IOptions<EnrichmentOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    public ExternalIdSource Source => ExternalIdSource.Tmdb;

    // For movies/series TMDB is primary; for anime it complements AniList (priority 10).
    public int Priority => 15;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.Tmdb)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.TmdbApiKey))
            return null;

        var kind = work.MediaType == MediaType.Movie ? "movie" : "tv";
        var url = $"https://api.themoviedb.org/3/{kind}/{externalId}" +
                  $"?api_key={_options.TmdbApiKey}&append_to_response=credits";

        var client = _factory.CreateClient(HttpClientName);
        var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);
        var d = doc.RootElement;

        // Movies use title/release_date; series use name/first_air_date.
        var isMovie = d.TryGetProperty("title", out _);
        data.CanonicalTitle = GetString(d, isMovie ? "title" : "name");
        data.OriginalTitle = GetString(d, isMovie ? "original_title" : "original_name");
        data.Overview = GetString(d, "overview");
        data.ReleaseYear = YearOf(GetString(d, isMovie ? "release_date" : "first_air_date"));

        if (!isMovie)
        {
            if (d.TryGetProperty("number_of_episodes", out var ep) && ep.ValueKind == JsonValueKind.Number)
                data.EpisodeCount = ep.GetInt32();
            if (d.TryGetProperty("number_of_seasons", out var se) && se.ValueKind == JsonValueKind.Number)
                data.SeasonCount = se.GetInt32();
            if (d.TryGetProperty("networks", out var nets) && nets.ValueKind == JsonValueKind.Array)
                data.Network = GetString(nets.EnumerateArray().FirstOrDefault(), "name");
        }

        if (d.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            foreach (var g in genres.EnumerateArray())
                if (GetString(g, "name") is { } name) data.Genres.Add(name);

        ParseCredits(d, data);

        if (GetString(d, "poster_path") is { } poster)
            data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = ImageBase + poster, Source = "tmdb", Score = 90 });
        if (GetString(d, "backdrop_path") is { } backdrop)
            data.Images.Add(new NormalizedImage { Type = ImageType.Backdrop, Url = ImageBase + backdrop, Source = "tmdb", Score = 90 });

        return data;
    }

    // Cast (top-billed) + key crew from append_to_response=credits.
    private static void ParseCredits(JsonElement d, NormalizedWorkData data)
    {
        if (!d.TryGetProperty("credits", out var credits) || credits.ValueKind != JsonValueKind.Object)
            return;

        if (credits.TryGetProperty("cast", out var cast) && cast.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in cast.EnumerateArray().Take(20))
            {
                if (GetString(member, "name") is not { } name)
                    continue;
                data.Credits.Add(new NormalizedCredit
                {
                    Name = name,
                    Role = CreditRole.Actor,
                    Character = GetString(member, "character"),
                    ImageUrl = GetString(member, "profile_path") is { } p ? ImageBase + p : null,
                    Order = member.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number
                        ? o.GetInt32() : int.MaxValue
                });
            }
        }

        if (credits.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
        {
            var order = 1000; // crew after the cast
            foreach (var member in crew.EnumerateArray())
            {
                if (GetString(member, "name") is not { } name)
                    continue;
                var role = GetString(member, "job")?.ToLowerInvariant() switch
                {
                    "director" => CreditRole.Director,
                    "screenplay" or "writer" or "story" => CreditRole.Writer,
                    "producer" or "executive producer" => CreditRole.Producer,
                    "original music composer" or "music" => CreditRole.Composer,
                    _ => CreditRole.Unknown
                };
                if (role == CreditRole.Unknown)
                    continue;
                data.Credits.Add(new NormalizedCredit
                {
                    Name = name,
                    Role = role,
                    ImageUrl = GetString(member, "profile_path") is { } p ? ImageBase + p : null,
                    Order = order++
                });
            }
        }
    }

    private static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
