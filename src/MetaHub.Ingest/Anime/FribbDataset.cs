using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// One row of the Fribb/anime-lists "full" mapping, merged on the AniDB id. It adds the
/// film/series-world identifiers (TVDB/TMDB/IMDb) that Jellyfin and artwork providers need.
/// Source ids are typed inconsistently (numbers, strings, or nested objects), so every id is
/// read leniently.
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

    // Fribb names this "tvdb_id" (not "thetvdb_id").
    [JsonPropertyName("tvdb_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? TvdbId { get; set; }

    // Fribb encodes TMDB ids as an object, e.g. {"tv": 26209} or {"movie": 12345}.
    [JsonPropertyName("themoviedb_id")]
    [JsonConverter(typeof(FribbObjectIdConverter))]
    public string? TmdbId { get; set; }

    [JsonPropertyName("imdb_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ImdbId { get; set; }
}

/// <summary>
/// Reads a JSON value that may be a number, string, null, or an unexpected object/array into
/// a string. Fribb mixes types across fields/rows, so a tolerant converter avoids parse
/// failures; non-scalar values are read as null rather than throwing.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // ParseValue fully consumes the current value regardless of shape (scalar/object/array)
        // and is safe across buffer boundaries during streaming deserialization.
        using var doc = JsonDocument.ParseValue(ref reader);
        return ScalarToString(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }

    internal static string? ScalarToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l)
            ? l.ToString(CultureInfo.InvariantCulture)
            : el.GetDouble().ToString(CultureInfo.InvariantCulture),
        _ => null
    };
}

/// <summary>
/// Reads a Fribb id that is wrapped in an object such as {"tv": 26209} or {"movie": 12345}
/// (used for themoviedb_id), returning the inner numeric/string id. Plain scalars and null are
/// also accepted; anything else reads as null.
/// </summary>
public class FribbObjectIdConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var el = doc.RootElement;

        if (el.ValueKind == JsonValueKind.Object)
        {
            // Prefer the TV id (anime series), then movie, then any scalar member.
            foreach (var name in new[] { "tv", "movie" })
            {
                if (el.TryGetProperty(name, out var preferred)
                    && FlexibleStringConverter.ScalarToString(preferred) is { } id)
                {
                    return id;
                }
            }

            foreach (var prop in el.EnumerateObject())
            {
                if (FlexibleStringConverter.ScalarToString(prop.Value) is { } id)
                {
                    return id;
                }
            }

            return null;
        }

        return FlexibleStringConverter.ScalarToString(el);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
