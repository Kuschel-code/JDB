namespace MetaHub.Ingest.Anime;

/// <summary>
/// Abstracts where dataset bytes come from so ingest can be tested against local
/// fixtures instead of the network.
/// </summary>
public interface IDatasetSource
{
    Task<Stream> OpenManamiAsync(CancellationToken ct = default);
    Task<Stream> OpenFribbAsync(CancellationToken ct = default);
    Task<Stream> OpenArmAsync(CancellationToken ct = default);
}
