using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// One row of the Fribb/anime-lists "full" mapping, merged on the AniDB id. It adds the
/// film/series-world identifiers (TVDB/TMDB/IMDb) that Jellyfin and artwork providers need.
/// Numeric ids are sometimes strings in the source, so they are read leniently.
/// </summary>
public class FribbEntry
{
    [JsonPropertyName("anidb_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? AniDbId { get; set; }

    [JsonPropertyName("anilist_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? AniListId { get; set; }

    [JsonPropertyName("mal_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? MalId { get; set; }

    [JsonPropertyName("kitsu_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? KitsuId { get; set; }

    [JsonPropertyName("thetvdb_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? TvdbId { get; set; }

    [JsonPropertyName("themoviedb_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? TmdbId { get; set; }

    [JsonPropertyName("imdb_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ImdbId { get; set; }
}

/// <summary>
/// Reads a JSON value that may be a number, string, or null into a string.
/// Fribb mixes types across fields/rows, so a tolerant converter avoids parse failures.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var l)) return l.ToString();
                return reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
