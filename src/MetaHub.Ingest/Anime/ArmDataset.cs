using System.Text.Json.Serialization;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// One row of the kawaiioverflow/arm mapping, which links MAL/AniList ids to the Japanese
/// databases Annict (annict.com) and Syoboi Calendar (cal.syoboi.jp).
/// </summary>
public class ArmEntry
{
    [JsonPropertyName("mal_id")]
    public int? MalId { get; set; }

    [JsonPropertyName("anilist_id")]
    public int? AniListId { get; set; }

    [JsonPropertyName("annict_id")]
    public int? AnnictId { get; set; }

    [JsonPropertyName("syobocal_tid")]
    public int? SyobocalTid { get; set; }
}
