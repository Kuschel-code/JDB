using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MetaHub.Domain;
using MetaHub.Domain.Entities;

namespace MetaHub.Enrichment.CustomSources;

/// <summary>
/// Reads metadata for a work from the databases the user hosts themselves — a folder of
/// JSON/NFO files, or their own HTTP endpoint. Unlike the built-in providers these are not
/// keyed by an external id: a self-hosted database is matched by <i>title</i> (canonical,
/// original or any known translation), which is what a folder tree naturally looks like.
///
/// Results are merged by <see cref="WorkMerger"/> like any other provider, at the priority the
/// user configured (default: ahead of the built-ins).
/// </summary>
public class CustomSourceService
{
    public const string HttpClientName = "metahub-custom";

    /// <summary>Metadata file names looked for inside a matched folder, in order.</summary>
    private static readonly string[] MetadataFileNames =
        { "metahub.json", "metadata.json", "info.json", "movie.nfo", "tvshow.nfo", "album.nfo", "book.nfo" };

    private readonly IHttpClientFactory _factory;
    private readonly EnrichmentOptions _options;
    private readonly ILogger<CustomSourceService> _log;

    /// <summary>Normalized folder name → full path, built once per root and reused across works.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _folderIndex = new(StringComparer.OrdinalIgnoreCase);

    public CustomSourceService(
        IHttpClientFactory factory, IOptions<EnrichmentOptions> options, ILogger<CustomSourceService> log)
    {
        _factory = factory;
        _options = options.Value;
        _log = log;
    }

    /// <summary>True when at least one source is configured (lets callers skip the work entirely).</summary>
    public bool HasSources => _options.CustomSources.Count > 0;

    /// <summary>
    /// Queries every configured source for <paramref name="work"/>. Each result carries the
    /// source's priority so the caller can merge it in the right order. Failures are logged and
    /// skipped — a broken custom source never breaks enrichment.
    /// </summary>
    public async Task<IReadOnlyList<(int Priority, NormalizedWorkData Data)>> FetchAsync(
        Work work, CancellationToken ct = default)
    {
        var results = new List<(int, NormalizedWorkData)>();
        if (!HasSources)
            return results;

        var names = NameCandidates(work);
        if (names.Count == 0)
            return results;

        foreach (var source in _options.CustomSources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var data = source.Kind == CustomSourceKind.Folder
                    ? ReadFromFolder(source, names)
                    : await ReadFromHttpAsync(source, work, names, ct).ConfigureAwait(false);

                if (data is not null)
                    results.Add((source.Priority, data));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Custom source {Source} failed for work {WorkId}", source.Name, work.Id);
            }
        }

        return results;
    }

    /// <summary>Every title the work is known by — a folder may be named after any of them.</summary>
    private static List<string> NameCandidates(Work work)
    {
        var names = new List<string?> { work.CanonicalTitle, work.OriginalTitle };
        names.AddRange(work.TitleTranslations.Values);

        return names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // --- folder sources ---

    private NormalizedWorkData? ReadFromFolder(CustomSource source, IReadOnlyList<string> names)
    {
        if (!Directory.Exists(source.Location))
        {
            _log.LogWarning("Custom source {Source}: folder {Path} does not exist", source.Name, source.Location);
            return null;
        }

        foreach (var name in names)
        {
            // A file named after the work, directly in the root ("<root>/Title.json").
            foreach (var extension in new[] { ".json", ".nfo" })
            {
                var file = Path.Combine(source.Location, Sanitize(name) + extension);
                if (File.Exists(file))
                    return ReadFile(file);
            }

            // A folder named after the work, holding the metadata file (and its artwork).
            if (FindFolder(source.Location, name) is { } folder && ReadFolderDocument(folder) is { } fromFolder)
                return fromFolder;
        }

        return null;
    }

    /// <summary>Finds a subfolder whose name matches the title, ignoring case and punctuation.</summary>
    private string? FindFolder(string root, string name)
    {
        if (!_folderIndex.TryGetValue(root, out var index))
        {
            index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var key = TitleNormalization.Normalize(Path.GetFileName(directory));
                if (key.Length > 0)
                    index.TryAdd(key, directory);
            }
            _folderIndex[root] = index;
        }

        return index.TryGetValue(TitleNormalization.Normalize(name), out var match) ? match : null;
    }

    private NormalizedWorkData? ReadFolderDocument(string folder)
    {
        foreach (var candidate in MetadataFileNames)
        {
            var file = Path.Combine(folder, candidate);
            if (File.Exists(file))
                return ReadFile(file);
        }

        // Fall back to any single .json/.nfo file in the folder.
        var any = Directory.EnumerateFiles(folder, "*.json").Concat(Directory.EnumerateFiles(folder, "*.nfo"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return any is null ? null : ReadFile(any);
    }

    private NormalizedWorkData ReadFile(string path)
    {
        // Artwork referenced relatively lives next to the metadata file.
        var data = CustomSourceParser.Parse(File.ReadAllText(path), Path.GetDirectoryName(path));
        _log.LogDebug("Custom source read {File}", path);
        return data;
    }

    /// <summary>Strips the characters a title may contain but a file name may not.</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
    }

    // --- HTTP sources ---

    private async Task<NormalizedWorkData?> ReadFromHttpAsync(
        CustomSource source, Work work, IReadOnlyList<string> names, CancellationToken ct)
    {
        var client = _factory.CreateClient(HttpClientName);

        var query = new List<string>
        {
            "title=" + Uri.EscapeDataString(names[0]),
            "type=" + Uri.EscapeDataString(work.MediaType.ToString())
        };
        if (work.ReleaseYear is { } year)
            query.Add("year=" + year);
        if (!string.IsNullOrWhiteSpace(work.OriginalTitle))
            query.Add("originalTitle=" + Uri.EscapeDataString(work.OriginalTitle!));

        // Pass the ids we already know so a smarter endpoint can match exactly instead of by title.
        foreach (var id in work.ExternalIds)
            query.Add(id.Source.ToString().ToLowerInvariant() + "=" + Uri.EscapeDataString(id.ExternalValue));

        var separator = source.Location.Contains('?') ? "&" : "?";
        var url = source.Location + separator + string.Join("&", query);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(source.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", source.ApiKey);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 404 simply means "not in this database"; anything else is worth a log line.
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                _log.LogWarning("Custom source {Source} returned {Status} for {Title}",
                    source.Name, (int)response.StatusCode, names[0]);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? null : CustomSourceParser.Parse(body, source.Location);
    }
}
