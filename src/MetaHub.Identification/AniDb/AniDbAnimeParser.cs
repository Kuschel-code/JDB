using System.Globalization;
using System.Xml.Linq;

namespace MetaHub.Identification.AniDb;

/// <summary>
/// Parses an AniDB HTTP anime XML response into <see cref="AniDbAnime"/>.
/// Robust against missing elements — every field is optional.
/// </summary>
public static class AniDbAnimeParser
{
    public static AniDbAnime Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;

        var titles = ParseTitles(root);
        var mainTitle = titles.FirstOrDefault(t => t.Type == "main")?.Text;
        var originalTitle = titles.FirstOrDefault(t => t.Type == "official" && t.Lang == "ja")?.Text
                            ?? titles.FirstOrDefault(t => t.Lang == "ja")?.Text;

        int.TryParse(root.Attribute("id")?.Value, out var aid);

        double? rating = null;
        var ratingEl = root.Element("ratings")?.Element("permanent");
        if (ratingEl is not null && double.TryParse(ratingEl.Value, CultureInfo.InvariantCulture, out var r))
            rating = r;

        int? episodeCount = null;
        var ecEl = root.Element("episodecount");
        if (ecEl is not null && int.TryParse(ecEl.Value, out var ec))
            episodeCount = ec;

        return new AniDbAnime
        {
            Aid = aid,
            MainTitle = mainTitle,
            OriginalTitle = originalTitle,
            Titles = titles,
            Episodes = ParseEpisodes(root),
            Characters = ParseCharacters(root),
            RelatedAnime = ParseRelated(root),
            Tags = ParseTags(root),
            Rating = rating,
            EpisodeCount = episodeCount,
            Type = root.Element("type")?.Value,
            StartDate = root.Element("startdate")?.Value,
            Description = root.Element("description")?.Value,
            PictureUrl = BuildPictureUrl(root.Element("picture")?.Value)
        };
    }

    /// <summary>Returns true if the XML is an AniDB error response, with the error text in <paramref name="error"/>.</summary>
    public static bool IsError(string xml, out string error)
    {
        error = string.Empty;
        try
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root?.Name.LocalName == "error")
            {
                error = doc.Root.Value.Trim();
                return true;
            }
            return false;
        }
        catch
        {
            error = "Unparseable response";
            return true;
        }
    }

    private static List<AniDbTitle> ParseTitles(XElement root)
    {
        var result = new List<AniDbTitle>();
        var titles = root.Element("titles");
        if (titles is null) return result;

        foreach (var t in titles.Elements("title"))
        {
            var lang = t.Attribute(XNamespace.Xml + "lang")?.Value;
            result.Add(new AniDbTitle
            {
                Text = t.Value.Trim(),
                Lang = lang,
                Type = t.Attribute("type")?.Value
            });
        }

        return result;
    }

    private static List<AniDbEpisode> ParseEpisodes(XElement root)
    {
        var result = new List<AniDbEpisode>();
        var episodes = root.Element("episodes");
        if (episodes is null) return result;

        foreach (var ep in episodes.Elements("episode"))
        {
            if (!int.TryParse(ep.Attribute("id")?.Value, out var id))
                continue;

            var epno = ep.Element("epno");
            var rawEpno = epno?.Value?.Trim() ?? "";
            int.TryParse(epno?.Attribute("type")?.Value, out var epnoType);

            string? title = null;
            string? titleEn = null;
            foreach (var t in ep.Elements("title"))
            {
                var lang = t.Attribute(XNamespace.Xml + "lang")?.Value;
                if (lang == "en") titleEn = t.Value.Trim();
                if (lang == "ja") title = t.Value.Trim();
            }

            title ??= titleEn;

            DateOnly? airDate = null;
            var adEl = ep.Element("airdate");
            if (adEl is not null && DateOnly.TryParse(adEl.Value, out var ad))
                airDate = ad;

            int? length = null;
            var lenEl = ep.Element("length");
            if (lenEl is not null && int.TryParse(lenEl.Value, out var len))
                length = len;

            result.Add(new AniDbEpisode
            {
                Id = id,
                RawEpno = rawEpno,
                EpnoType = epnoType,
                Title = title,
                TitleEn = titleEn,
                AirDate = airDate,
                Length = length
            });
        }

        return result;
    }

    private static List<AniDbCharacter> ParseCharacters(XElement root)
    {
        var result = new List<AniDbCharacter>();
        var characters = root.Element("characters");
        if (characters is null) return result;

        foreach (var c in characters.Elements("character"))
        {
            var name = c.Element("name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var charPic = BuildPictureUrl(c.Element("characterimage")?.Element("normal")?.Value);
            var seiyuu = c.Element("seiyuu");
            var seiyuuName = seiyuu?.Value?.Trim();
            var seiyuuPic = seiyuu?.Attribute("picture")?.Value;
            if (seiyuuPic is not null)
                seiyuuPic = $"https://cdn.anidb.net/images/main/{seiyuuPic}";

            result.Add(new AniDbCharacter
            {
                Name = name,
                ImageUrl = charPic,
                SeiyuuName = string.IsNullOrWhiteSpace(seiyuuName) ? null : seiyuuName,
                SeiyuuImageUrl = seiyuuPic
            });
        }

        return result;
    }

    private static List<AniDbRelatedAnime> ParseRelated(XElement root)
    {
        var result = new List<AniDbRelatedAnime>();
        var related = root.Element("relatedanime");
        if (related is null) return result;

        foreach (var a in related.Elements("anime"))
        {
            if (!int.TryParse(a.Attribute("id")?.Value, out var aid))
                continue;

            result.Add(new AniDbRelatedAnime
            {
                Aid = aid,
                Type = a.Attribute("type")?.Value ?? "Other",
                Title = a.Value.Trim()
            });
        }

        return result;
    }

    private static List<string> ParseTags(XElement root)
    {
        var result = new List<string>();
        var tags = root.Element("tags");
        if (tags is null) return result;

        foreach (var t in tags.Elements("tag"))
        {
            var name = t.Element("name")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name);
        }

        return result;
    }

    private static string? BuildPictureUrl(string? filename)
        => string.IsNullOrWhiteSpace(filename) ? null : $"https://cdn.anidb.net/images/main/{filename}";
}
