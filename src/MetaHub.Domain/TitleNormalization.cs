namespace MetaHub.Domain;

/// <summary>
/// Shared title normalization for name-based matching: lowercase a title and strip the common
/// separators so "22/7" and "227", or "Saiki K." and "Saiki K", compare equal. Used both when
/// building a work's searchable title set at ingest time and when resolving an item by name, so
/// the two always agree. Must mirror the SQL Replace chain in the name resolver.
/// </summary>
public static class TitleNormalization
{
    /// <summary>The separators removed from a title before comparison. Includes brackets and
    /// parentheses: manami/AniList canonicals wrap a part name in square brackets
    /// ("… [Heaven's Feel] I. presage flower") while libraries are filed with round parentheses or
    /// none, and the two must compare equal. Mirrored by the SQL Replace chain in
    /// <see cref="MetaHub.Jellyfin.Api.MetaHubBackend"/>.ResolveByNameAsync — keep both in sync.</summary>
    public static readonly string[] Separators =
        { " ", "/", "-", ":", ".", ",", "'", "!", "?", "_", "(", ")", "[", "]", "{", "}" };

    /// <summary>Lowercases <paramref name="title"/> (ASCII A–Z only) and removes every separator.
    /// The ASCII-only fold mirrors SQLite's <c>lower()</c> exactly, so the C# side of a name match
    /// compares equal to the EF-translated query even for titles with a non-ASCII uppercase letter
    /// (Ō, Ä, Cyrillic…); a full-Unicode <c>ToLowerInvariant</c> would silently never match those.</summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return string.Empty;
        var s = AsciiLower(title);
        foreach (var sep in Separators)
            s = s.Replace(sep, string.Empty);
        return s;
    }

    /// <summary>Lowercases ASCII A–Z only, exactly like SQLite's built-in <c>lower()</c>. EF Core
    /// translates <c>string.ToLower()</c> to that ASCII-only function, so the C# side of every
    /// title comparison must fold the same way or non-ASCII-uppercase titles never match.</summary>
    public static string AsciiLower(string s)
    {
        char[]? buf = null;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is >= 'A' and <= 'Z')
            {
                buf ??= s.ToCharArray();
                buf[i] = (char)(s[i] + 32);
            }
        }
        return buf is null ? s : new string(buf);
    }

    /// <summary>
    /// Builds the pipe-delimited, normalized "search titles" blob stored on a work — every known
    /// title (primary + synonyms) wrapped so an exact segment can be matched with
    /// <c>SearchTitles.Contains("|" + Normalize(name) + "|")</c> without substring false positives.
    /// Returns "" when no usable title is given.
    /// </summary>
    public static string BuildSearchTitles(IEnumerable<string?> titles)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var t in titles)
        {
            var norm = Normalize(t);
            if (norm.Length > 0 && seen.Add(norm))
                parts.Add(norm);
        }
        return parts.Count == 0 ? string.Empty : "|" + string.Join("|", parts) + "|";
    }

    /// <summary>The needle that matches a single title inside a <see cref="BuildSearchTitles"/> blob.</summary>
    public static string SearchNeedle(string? title) => "|" + Normalize(title) + "|";
}
