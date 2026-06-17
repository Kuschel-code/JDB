using System.Net.Http.Json;
using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.Providers;

/// <summary>
/// AniList provider (GraphQL, no API key, generous limits). Most authoritative for anime,
/// so it has the lowest priority number.
/// </summary>
public class AniListProvider : IMetadataProvider
{
    public const string HttpClientName = "anilist";
    private const string Endpoint = "https://graphql.anilist.co";

    private const string Query = @"
query ($id: Int) {
  Media(id: $id, type: ANIME) {
    title { romaji english native }
    description(asHtml: false)
    status
    startDate { year }
    episodes
    genres
    coverImage { extraLarge color }
    bannerImage
    characters(sort: [ROLE, RELEVANCE], perPage: 15) {
      edges {
        node { name { full } image { large } }
        voiceActors(language: JAPANESE) { name { full } image { large } }
      }
    }
    staff(sort: RELEVANCE, perPage: 10) {
      edges {
        role
        node { name { full } image { large } }
      }
    }
  }
}";

    private readonly IHttpClientFactory _factory;

    public AniListProvider(IHttpClientFactory factory) => _factory = factory;

    public ExternalIdSource Source => ExternalIdSource.AniList;
    public int Priority => 10;

    public string? GetExternalId(Work work)
        => work.ExternalIds.FirstOrDefault(x => x.Source == ExternalIdSource.AniList)?.ExternalValue;

    public async Task<string?> FetchRawAsync(Work work, string externalId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalId, out var id))
            return null;

        var client = _factory.CreateClient(HttpClientName);
        var request = new { query = Query, variables = new { id } };
        var response = await client.PostAsJsonAsync(Endpoint, request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    public NormalizedWorkData Parse(string rawBody)
    {
        var data = new NormalizedWorkData { Source = Source };
        using var doc = JsonDocument.Parse(rawBody);

        if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
            !dataEl.TryGetProperty("Media", out var media) ||
            media.ValueKind != JsonValueKind.Object)
            return data;

        if (media.TryGetProperty("title", out var title))
        {
            var english = GetString(title, "english");
            var romaji = GetString(title, "romaji");
            var native = GetString(title, "native");
            data.CanonicalTitle = english ?? romaji;
            data.OriginalTitle = native ?? romaji;

            // Localized titles so the serve layer can show the viewer's language instead of romaji.
            if (!string.IsNullOrWhiteSpace(english)) data.TitleTranslations["en"] = english;
            if (!string.IsNullOrWhiteSpace(native)) data.TitleTranslations["ja"] = native;
        }

        var description = GetString(media, "description");
        if (description is not null)
        {
            // AniList descriptions may contain simple HTML line breaks.
            data.Overview = description.Replace("<br>", "\n").Replace("<br/>", "\n").Trim();
        }

        if (media.TryGetProperty("startDate", out var start) && start.TryGetProperty("year", out var year)
            && year.ValueKind == JsonValueKind.Number)
            data.ReleaseYear = year.GetInt32();

        data.Status = MapStatus(GetString(media, "status"));

        if (media.TryGetProperty("episodes", out var eps) && eps.ValueKind == JsonValueKind.Number)
            data.EpisodeCount = eps.GetInt32();

        if (media.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            foreach (var g in genres.EnumerateArray())
                if (g.GetString() is { } name) data.Genres.Add(name);

        ParseCredits(media, data);

        if (media.TryGetProperty("coverImage", out var cover) && GetString(cover, "extraLarge") is { } poster)
            data.Images.Add(new NormalizedImage { Type = ImageType.Poster, Url = poster, Source = "anilist", Score = 80 });

        if (GetString(media, "bannerImage") is { } banner)
            data.Images.Add(new NormalizedImage { Type = ImageType.Backdrop, Url = banner, Source = "anilist", Score = 80 });

        return data;
    }

    // Voice actors (with their character) and key staff (director, composer, …).
    private static void ParseCredits(JsonElement media, NormalizedWorkData data)
    {
        var order = 0;
        if (media.TryGetProperty("characters", out var characters) &&
            characters.TryGetProperty("edges", out var charEdges) &&
            charEdges.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in charEdges.EnumerateArray())
            {
                var character = edge.TryGetProperty("node", out var charNode) ? FullName(charNode) : null;
                if (!edge.TryGetProperty("voiceActors", out var actors) || actors.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var actor in actors.EnumerateArray())
                {
                    if (FullName(actor) is not { } name)
                        continue;
                    data.Credits.Add(new NormalizedCredit
                    {
                        Name = name,
                        Role = CreditRole.VoiceActor,
                        Character = character,
                        ImageUrl = ImageLarge(actor),
                        Order = order++
                    });
                }
            }
        }

        if (media.TryGetProperty("staff", out var staff) &&
            staff.TryGetProperty("edges", out var staffEdges) &&
            staffEdges.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in staffEdges.EnumerateArray())
            {
                if (!edge.TryGetProperty("node", out var node) || FullName(node) is not { } name)
                    continue;

                var roleText = GetString(edge, "role") ?? string.Empty;
                data.Credits.Add(new NormalizedCredit
                {
                    Name = name,
                    Role = MapStaffRole(roleText),
                    Character = null,
                    ImageUrl = ImageLarge(node),
                    Order = order++
                });
            }
        }
    }

    private static CreditRole MapStaffRole(string role)
    {
        var r = role.ToLowerInvariant();
        if (r.Contains("director")) return CreditRole.Director;
        if (r.Contains("music") || r.Contains("composer")) return CreditRole.Composer;
        if (r.Contains("original creator") || r.Contains("original story") || r.Contains("script")
            || r.Contains("series composition")) return CreditRole.Writer;
        if (r.Contains("producer")) return CreditRole.Producer;
        return CreditRole.Unknown;
    }

    private static string? FullName(JsonElement el)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out var name)
           && name.ValueKind == JsonValueKind.Object
            ? GetString(name, "full") : null;

    private static string? ImageLarge(JsonElement el)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty("image", out var image)
           && image.ValueKind == JsonValueKind.Object
            ? GetString(image, "large") : null;

    private static WorkStatus MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "FINISHED" => WorkStatus.Finished,
        "RELEASING" => WorkStatus.Ongoing,
        "NOT_YET_RELEASED" => WorkStatus.Announced,
        "CANCELLED" => WorkStatus.Cancelled,
        "HIATUS" => WorkStatus.OnHiatus,
        _ => WorkStatus.Unknown
    };

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
