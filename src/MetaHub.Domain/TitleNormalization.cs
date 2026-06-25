namespace MetaHub.Domain;

/// <summary>
/// Shared title normalization for name-based matching: lowercase a title and strip the common
/// separators so "22/7" and "227", or "Saiki K." and "Saiki K", compare equal. Used both when
/// building a work's searchable title set at ingest time and when resolving an item by name, so
/// the two always agree. Must mirror the SQL Replace chain in the name resolver.
/// </summary>
public static class TitleNormalization
{
    /// <summary>The separators removed from a title before comparison.</summary>
    public static readonly string[] Separators =
        { " ", "/", "-", ":", ".", ",", "'", "!", "?", "_" };

    /// <summary>Lowercases <paramref name="title"/> and removes every separator.</summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return string.Empty;
        var s = title.ToLowerInvariant();
        foreach (var sep in Separators)
            s = s.Replace(sep, string.Empty);
        return s;
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
