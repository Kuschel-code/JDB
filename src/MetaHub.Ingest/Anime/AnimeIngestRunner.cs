using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MetaHub.Ingest.Anime;

/// <summary>
/// Orchestrates a full anime ingest: stream the datasets, deserialize them, then upsert
/// via <see cref="AnimeIngestService"/>. Streaming the (large) JSON keeps memory bounded.
/// </summary>
public class AnimeIngestRunner
{
    private readonly IDatasetSource _source;
    private readonly AnimeIngestService _service;
    private readonly ILogger<AnimeIngestRunner> _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AnimeIngestRunner(IDatasetSource source, AnimeIngestService service, ILogger<AnimeIngestRunner> log)
    {
        _source = source;
        _service = service;
        _log = log;
    }

    public async Task<(IngestResult Manami, IngestResult Fribb)> RunAsync(
        CancellationToken ct = default, IProgress<double>? progress = null)
    {
        _log.LogInformation("Starting anime ingest (manami + Fribb)...");
        progress?.Report(5);

        await using var manamiStream = await _source.OpenManamiAsync(ct);
        var dataset = await JsonSerializer.DeserializeAsync<ManamiDataset>(manamiStream, JsonOptions, ct)
                      ?? new ManamiDataset();
        var manamiResult = await _service.IngestManamiAsync(dataset, ct);
        progress?.Report(60);

        await using var fribbStream = await _source.OpenFribbAsync(ct);
        var fribb = await JsonSerializer.DeserializeAsync<List<FribbEntry>>(fribbStream, JsonOptions, ct)
                    ?? new List<FribbEntry>();
        var fribbResult = await _service.MergeFribbAsync(fribb, ct);
        progress?.Report(85);

        // Japanese database ids (Annict / Syoboi Calendar) via the ARM mapping — best effort,
        // the core ingest stays usable when the extra dataset is unavailable.
        try
        {
            await using var armStream = await _source.OpenArmAsync(ct);
            var arm = await JsonSerializer.DeserializeAsync<List<ArmEntry>>(armStream, JsonOptions, ct)
                      ?? new List<ArmEntry>();
            await _service.MergeArmAsync(arm, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "ARM (Japanese db) mapping merge failed; continuing without it");
        }

        return (manamiResult, fribbResult);
    }
}
