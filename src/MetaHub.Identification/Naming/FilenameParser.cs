using System.Text;
using System.Text.RegularExpressions;

namespace MetaHub.Identification.Naming;

/// <summary>
/// Parses a cleaned title, year and (for series) season/episode out of a media filename.
/// Movies and series have no universal hash database, so name parsing feeds a TMDB/TVDB
/// search — quality depends on tidy naming.
/// </summary>
public static partial class FilenameParser
{
    [GeneratedRegex(@"[Ss](?<s>\d{1,2})[\s._-]*[Ee](?<e>\d{1,3})", RegexOptions.Compiled)]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"(?<!\d)(?<year>19\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled)]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    public static ParsedFileName Parse(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        int? season = null, episode = null;
        var se = SeasonEpisodeRegex().Match(name);
        if (se.Success)
        {
            season = int.Parse(se.Groups["s"].Value);
            episode = int.Parse(se.Groups["e"].Value);
        }

        // A year is only meaningful before any SxxEyy marker (episode titles can contain years).
        int? year = null;
        var searchRegion = se.Success ? name[..se.Index] : name;
        var yearMatch = YearRegex().Match(searchRegion);
        if (yearMatch.Success)
            year = int.Parse(yearMatch.Groups["year"].Value);

        // Title = everything before the first strong marker (SxxEyy or year), cleaned up.
        var cut = name.Length;
        if (se.Success) cut = Math.Min(cut, se.Index);
        if (yearMatch.Success) cut = Math.Min(cut, yearMatch.Index);

        var title = name[..cut]
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Trim(' ', '-', '(', '[');
        title = Sanitize(title);

        return new ParsedFileName(title, year, season, episode);
    }

    /// <summary>
    /// Removes characters that must never appear in a title — C0/C1 control codes, the Unicode
    /// replacement char (U+FFFD, what an undecodable byte in a folder name becomes), and
    /// zero-width / BOM marks — then collapses whitespace. Without this a folder whose name
    /// contains a broken byte leaks a "K box" (U+FFFD) character into the metadata title.
    /// </summary>
    public static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            int c = ch;
            bool drop =
                c <= 0x1F ||                       // C0 controls
                (c >= 0x7F && c <= 0x9F) ||        // DEL + C1 controls
                c == 0xFFFD ||                     // replacement char
                (c >= 0x200B && c <= 0x200D) ||    // zero-width space / (non-)joiner
                c == 0xFEFF;                       // BOM / zero-width no-break space
            if (!drop)
                sb.Append(ch);
        }

        return WhitespaceRegex().Replace(sb.ToString(), " ").Trim();
    }
}

/// <param name="IsEpisode">True when a season/episode marker was found.</param>
public record ParsedFileName(string Title, int? Year, int? Season, int? Episode)
{
    public bool IsEpisode => Season is not null && Episode is not null;
}
