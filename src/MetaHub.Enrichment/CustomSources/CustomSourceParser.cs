using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using MetaHub.Domain.Enums;

namespace MetaHub.Enrichment.CustomSources;

/// <summary>
/// Reads a user-hosted metadata document into <see cref="NormalizedWorkData"/>. Two formats are
/// accepted so people can reuse what they already have:
/// <list type="bullet">
/// <item><b>JSON</b> — MetaHub's own document shape (see the README); every field is optional.</item>
/// <item><b>NFO</b> — the Kodi/Jellyfin XML sidecar (<c>movie</c>, <c>tvshow</c>, <c>episodedetails</c>, …).</item>
/// </list>
/// Relative image paths are resolved against <c>baseDirectory</c> (folder sources) or the
/// document's base URL (HTTP sources), so a source can ship artwork next to its metadata.
/// </summary>
public static class CustomSourceParser
{
    /// <summary>Base artwork quality for a self-hosted source — above TMDB (90) because the user curated it.</summary>
    private const double DefaultImageScore = 95;

    /// <summary>Sniffs the format (JSON vs. XML/NFO) and parses accordingly.</summary>
    public static NormalizedWorkData Parse(string content, string? baseLocation = null)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[')
            ? ParseJson(content, baseLocation)
            : ParseNfo(content, baseLocation);
    }

    public static NormalizedWorkData ParseJson(string body, string? baseLocation = null)
    {
        var data = new NormalizedWorkData { Source = ExternalIdSource.Custom };
        using var doc = JsonDocument.Parse(body);

        var root = doc.RootElement;
        // Allow a bare array or a {"data": …} / {"work": …} envelope so a small API can answer
        // with a search result list without extra shaping.
        if (root.ValueKind == JsonValueKind.Array)
            root = root.EnumerateArray().FirstOrDefault();
        else if (Obj(root, "work") is { } w)
            root = w;
        else if (Obj(root, "data") is { } d)
            root = d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().FirstOrDefault() : d;

        if (root.ValueKind != JsonValueKind.Object)
            return data;

        data.CanonicalTitle = Str(root, "title", "canonicalTitle", "name");
        data.OriginalTitle = Str(root, "originalTitle", "original_title", "originaltitle");
        data.Overview = Str(root, "overview", "plot", "description", "summary");
        data.ReleaseYear = Int(root, "year", "releaseYear", "release_year")
                           ?? YearFromDate(Str(root, "premiered", "releaseDate", "first_air_date"));

        if (Str(root, "status") is { } status && Enum.TryParse<WorkStatus>(status, true, out var parsed))
            data.Status = parsed;

        foreach (var (key, value) in Map(root, "overviews", "overviewTranslations", "plots"))
            data.OverviewTranslations[key] = value;
        foreach (var (key, value) in Map(root, "titles", "titleTranslations"))
            data.TitleTranslations[key] = value;

        foreach (var genre in Strings(root, "genres", "genre", "tags"))
            data.Genres.Add(genre);

        // Series / anime
        data.EpisodeCount = Int(root, "episodeCount", "episodes", "episode_count");
        data.SeasonCount = Int(root, "seasonCount", "seasons", "season_count");
        data.Network = Str(root, "network", "studio");

        // Music
        data.AlbumType = Str(root, "albumType", "album_type");
        data.Label = Str(root, "label");
        data.TrackCount = Int(root, "trackCount", "tracks", "track_count");

        // Books
        data.Isbn13 = Str(root, "isbn13", "isbn");
        data.PageCount = Int(root, "pageCount", "pages", "page_count");
        data.Publisher = Str(root, "publisher");
        data.SeriesName = Str(root, "seriesName", "series");
        if (Str(root, "seriesIndex", "series_index") is { } idx
            && double.TryParse(idx, NumberStyles.Any, CultureInfo.InvariantCulture, out var seriesIndex))
            data.SeriesIndex = seriesIndex;
        else if (Num(root, "seriesIndex", "series_index") is { } numericIndex)
            data.SeriesIndex = numericIndex;

        ReadJsonImages(root, data, baseLocation);
        ReadJsonPeople(root, data, baseLocation);
        return data;
    }

    public static NormalizedWorkData ParseNfo(string xml, string? baseLocation = null)
    {
        var data = new NormalizedWorkData { Source = ExternalIdSource.Custom };
        var root = XDocument.Parse(xml).Root;
        if (root is null)
            return data;

        data.CanonicalTitle = El(root, "title");
        data.OriginalTitle = El(root, "originaltitle");
        data.Overview = El(root, "plot", "outline");
        data.ReleaseYear = ParseInt(El(root, "year")) ?? YearFromDate(El(root, "premiered", "releasedate"));
        data.Network = El(root, "studio");
        data.Publisher = El(root, "publisher");
        data.Isbn13 = El(root, "isbn");
        data.EpisodeCount = ParseInt(El(root, "episode"));

        if (El(root, "status") is { } status)
        {
            // Kodi writes "Continuing"/"Ended" for shows.
            data.Status = status.ToLowerInvariant() switch
            {
                "continuing" or "returning series" or "ongoing" => WorkStatus.Ongoing,
                "ended" or "finished" => WorkStatus.Finished,
                "canceled" or "cancelled" => WorkStatus.Cancelled,
                _ => Enum.TryParse<WorkStatus>(status, true, out var s) ? s : null
            };
        }

        foreach (var genre in root.Elements("genre").Select(e => e.Value.Trim()).Where(v => v.Length > 0))
            data.Genres.Add(genre);

        // <thumb aspect="poster|banner|logo|clearlogo">url</thumb> plus <fanart><thumb>url</thumb></fanart>.
        foreach (var thumb in root.Elements("thumb"))
            AddImage(data, ImageTypeFromAspect(thumb.Attribute("aspect")?.Value), thumb.Value, baseLocation);
        foreach (var thumb in root.Elements("fanart").Elements("thumb"))
            AddImage(data, ImageType.Backdrop, thumb.Value, baseLocation);

        var order = 0;
        foreach (var actor in root.Elements("actor"))
        {
            var name = El(actor, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            data.Credits.Add(new NormalizedCredit
            {
                Name = name!,
                Role = CreditRole.Actor,
                Character = El(actor, "role"),
                ImageUrl = ResolveUrl(El(actor, "thumb"), baseLocation),
                Order = ParseInt(El(actor, "order")) ?? order
            });
            order++;
        }

        AddNfoCrew(root, data, "director", CreditRole.Director);
        AddNfoCrew(root, data, "credits", CreditRole.Writer);
        AddNfoCrew(root, data, "writer", CreditRole.Writer);
        AddNfoCrew(root, data, "composer", CreditRole.Composer);
        AddNfoCrew(root, data, "author", CreditRole.Author);
        return data;
    }

    private static void AddNfoCrew(XElement root, NormalizedWorkData data, string element, CreditRole role)
    {
        foreach (var value in root.Elements(element).Select(e => e.Value.Trim()).Where(v => v.Length > 0))
            data.Credits.Add(new NormalizedCredit { Name = value, Role = role, Order = data.Credits.Count });
    }

    private static void ReadJsonImages(JsonElement root, NormalizedWorkData data, string? baseLocation)
    {
        // Shorthand keys for the common single-image cases.
        foreach (var (key, type) in new[]
                 {
                     ("poster", ImageType.Poster), ("cover", ImageType.Cover),
                     ("backdrop", ImageType.Backdrop), ("fanart", ImageType.Backdrop),
                     ("banner", ImageType.Banner), ("logo", ImageType.Logo), ("thumb", ImageType.Thumb)
                 })
        {
            if (Str(root, key) is { } url)
                AddImage(data, type, url, baseLocation);
        }

        if (Obj(root, "images") is not { } images)
            return;

        // Either an array of image objects, or an object keyed by type: {"poster": "…"}.
        if (images.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in images.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    AddImage(data, ImageTypeFromName(property.Name), property.Value.GetString(), baseLocation);
            return;
        }

        if (images.ValueKind != JsonValueKind.Array)
            return;

        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind == JsonValueKind.String)
            {
                AddImage(data, ImageType.Poster, image.GetString(), baseLocation);
                continue;
            }
            if (image.ValueKind != JsonValueKind.Object)
                continue;

            AddImage(
                data,
                ImageTypeFromName(Str(image, "type", "kind")),
                Str(image, "url", "path", "file"),
                baseLocation,
                lang: Str(image, "lang", "language"),
                width: Int(image, "width"),
                height: Int(image, "height"),
                score: Num(image, "score"));
        }
    }

    private static void ReadJsonPeople(JsonElement root, NormalizedWorkData data, string? baseLocation)
    {
        foreach (var key in new[] { "people", "credits", "cast", "actors" })
        {
            if (Obj(root, key) is not { ValueKind: JsonValueKind.Array } people)
                continue;

            foreach (var person in people.EnumerateArray())
            {
                if (person.ValueKind != JsonValueKind.Object)
                    continue;

                var name = Str(person, "name", "person");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var role = Str(person, "role", "job", "type");
                data.Credits.Add(new NormalizedCredit
                {
                    Name = name!,
                    Role = role is not null && Enum.TryParse<CreditRole>(role, true, out var parsed)
                        ? parsed
                        : key == "cast" || key == "actors" ? CreditRole.Actor : CreditRole.Unknown,
                    Character = Str(person, "character", "role_name", "as"),
                    ImageUrl = ResolveUrl(Str(person, "image", "thumb", "photo"), baseLocation),
                    Order = Int(person, "order") ?? data.Credits.Count
                });
            }
        }
    }

    private static void AddImage(
        NormalizedWorkData data, ImageType type, string? url, string? baseLocation,
        string? lang = null, int? width = null, int? height = null, double? score = null)
    {
        var resolved = ResolveUrl(url, baseLocation);
        if (resolved is null)
            return;

        data.Images.Add(new NormalizedImage
        {
            Type = type,
            Url = resolved,
            Lang = lang,
            Width = width,
            Height = height,
            Source = "custom",
            Score = score ?? DefaultImageScore
        });
    }

    /// <summary>
    /// Turns a possibly relative reference into something fetchable: absolute URLs and absolute
    /// paths pass through, relative ones are combined with the document's folder or base URL.
    /// </summary>
    internal static string? ResolveUrl(string? value, string? baseLocation)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var reference = value.Trim();
        if (reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return reference;

        if (string.IsNullOrWhiteSpace(baseLocation))
            return Path.IsPathRooted(reference) ? reference : null;

        if (baseLocation.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || baseLocation.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(new Uri(baseLocation), reference, out var absolute) ? absolute.ToString() : reference;

        return Path.IsPathRooted(reference) ? reference : Path.GetFullPath(Path.Combine(baseLocation, reference));
    }

    private static ImageType ImageTypeFromAspect(string? aspect) => ImageTypeFromName(aspect);

    private static ImageType ImageTypeFromName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "backdrop" or "fanart" or "background" => ImageType.Backdrop,
        "banner" => ImageType.Banner,
        "logo" or "clearlogo" => ImageType.Logo,
        "thumb" or "landscape" or "still" => ImageType.Thumb,
        "cover" => ImageType.Cover,
        _ => ImageType.Poster
    };

    // --- small JSON/XML helpers ---

    private static JsonElement? Obj(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
                return value;
        return null;
    }

    private static string? Str(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
                continue;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(text))
                return text!.Trim();
        }
        return null;
    }

    private static int? Int(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                return parsed;
        }
        return null;
    }

    private static double? Num(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                return number;
        }
        return null;
    }

    private static IEnumerable<string> Strings(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (Obj(root, name) is not { } value)
                continue;

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s && s.Trim().Length > 0)
                        yield return s.Trim();
            }
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } single)
            {
                // Allow a comma-separated list, which is what hand-written files tend to use.
                foreach (var part in single.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    yield return part;
            }
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> Map(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (Obj(root, name) is not { ValueKind: JsonValueKind.Object } value)
                continue;

            foreach (var property in value.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { } text && text.Trim().Length > 0)
                    yield return new KeyValuePair<string, string>(property.Name, text.Trim());
        }
    }

    private static string? El(XElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = root.Element(name)?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static int? YearFromDate(string? value)
        => value is { Length: >= 4 } && int.TryParse(value[..4], out var year) ? year : null;
}
