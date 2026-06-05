using System.Text.Json.Serialization;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// Shape of the manami-project/anime-offline-database JSON file.
/// </summary>
public class ManamiDataset
{
    [JsonPropertyName("data")]
    public List<ManamiEntry> Data { get; set; } = new();
}

public class ManamiEntry
{
    /// <summary>Provider URLs, e.g. "https://anidb.net/anime/1". These encode the cross-IDs.</summary>
    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = new();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>TV, MOVIE, OVA, ONA, SPECIAL, UNKNOWN.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("episodes")]
    public int Episodes { get; set; }

    /// <summary>FINISHED, ONGOING, UPCOMING, UNKNOWN.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("animeSeason")]
    public ManamiSeason? AnimeSeason { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("synonyms")]
    public List<string> Synonyms { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class ManamiSeason
{
    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }
}
