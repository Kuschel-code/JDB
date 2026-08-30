namespace MetaHub.Enrichment.CustomSources;

/// <summary>How the user's own databases rank against the built-in providers.</summary>
public enum CustomSourceMode
{
    /// <summary>Your data wins every field it supplies; the built-ins fill the rest. (Default.)</summary>
    Prefer = 0,
    /// <summary>The built-in providers lead; your data only fills what they left empty.</summary>
    Fallback = 1,
    /// <summary>
    /// Decide per entry: an entry that actually carries content (an overview, artwork or cast)
    /// is trusted and wins, while a stub — say a title and a year — steps behind the built-ins
    /// instead of overriding their better data with its scraps.
    /// </summary>
    Auto = 2
}

/// <summary>Where a user-defined metadata source lives.</summary>
public enum CustomSourceKind
{
    /// <summary>A directory on disk (local, NAS or network mount) holding metadata files.</summary>
    Folder = 0,
    /// <summary>A self-hosted HTTP endpoint returning MetaHub's JSON document shape.</summary>
    Http = 1
}

/// <summary>
/// A metadata database the user hosts themselves: either a folder of JSON/NFO files or an HTTP
/// endpoint. Configured as one line per source so it round-trips through the plugin's simple
/// string-array settings:
/// <code>
/// Name | Location | [Priority] | [ApiKey]
/// </code>
/// The kind is derived from the location (http:// or https:// → HTTP, otherwise a folder), and
/// everything but the location is optional:
/// <code>
/// My NAS         | /mnt/nas/metadata
/// Home API       | https://meta.lan/metahub | 3 | s3cret
/// /srv/anime-db
/// </code>
/// </summary>
public class CustomSource
{
    /// <summary>
    /// Priority given to a preferred source. Lower wins and the built-in providers sit at
    /// 10–30, so this places a self-hosted database ahead of all of them.
    /// </summary>
    public const int PreferPriority = 5;

    /// <summary>Priority given to a source that should only fill gaps — behind every built-in.</summary>
    public const int FallbackPriority = 1000;

    /// <summary>Display name, used in logs and in the settings UI.</summary>
    public string Name { get; set; } = string.Empty;

    public CustomSourceKind Kind { get; set; }

    /// <summary>Absolute folder path, or the base URL of the self-hosted endpoint.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Optional key, sent as the <c>X-Api-Key</c> header (HTTP sources only).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Explicit merge priority for this one source (lower wins, same scale as the built-in
    /// providers). <c>null</c> — the normal case — means "follow
    /// <see cref="EnrichmentOptions.CustomSourceMode"/>".
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>Parses one configuration line. Returns false for blank lines and comments (#).</summary>
    public static bool TryParse(string? line, out CustomSource source)
    {
        source = new CustomSource();
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var text = line.Trim();
        if (text.StartsWith('#'))
            return false;

        var parts = text.Split('|', StringSplitOptions.TrimEntries);

        // Normally "Name | Location"; with only one field (or an empty second one, e.g. a
        // trailing "|") that single field is the location and the source is named after it.
        var hasName = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]);
        var location = hasName ? parts[1] : parts[0];
        var name = hasName ? parts[0] : string.Empty;

        // A location is only usable when it is unambiguous: a URL, or an absolute path. A
        // relative path would resolve against Jellyfin's working directory, so reject it rather
        // than silently reading the wrong place.
        if (!IsUsableLocation(location))
            return false;

        if (parts.Length > 2 && int.TryParse(parts[2], out var priority))
            source.Priority = priority;   // explicit override; otherwise the mode decides
        if (parts.Length > 3)
            source.ApiKey = parts[3];

        source.Location = location;
        source.Kind = location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? CustomSourceKind.Http
            : CustomSourceKind.Folder;

        source.Name = string.IsNullOrWhiteSpace(name) ? DeriveName(location, source.Kind) : name;
        return true;
    }

    /// <summary>Parses a whole configuration block, skipping blank/comment/invalid lines.</summary>
    public static List<CustomSource> ParseAll(IEnumerable<string>? lines)
    {
        var list = new List<CustomSource>();
        if (lines is null)
            return list;

        foreach (var line in lines)
            if (TryParse(line, out var source))
                list.Add(source);

        return list;
    }

    /// <summary>True for an absolute URL or an absolute (rooted) filesystem path.</summary>
    private static bool IsUsableLocation(string? location)
        => !string.IsNullOrWhiteSpace(location)
           && (location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || Path.IsPathRooted(location));

    /// <summary>Names an unnamed source after its folder, or the host of its URL.</summary>
    private static string DeriveName(string location, CustomSourceKind kind)
    {
        if (kind == CustomSourceKind.Http)
            return Uri.TryCreate(location, UriKind.Absolute, out var uri) ? uri.Host : location;

        var trimmed = location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }

    public override string ToString()
        => $"{Name} ({Kind}: {Location}, priority {Priority?.ToString() ?? "by mode"})";
}
