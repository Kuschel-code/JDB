namespace MetaHub.Enrichment.Providers;

/// <summary>
/// Enforces MusicBrainz's documented ~1 request/second limit across every caller. Registered as
/// a singleton so it is shared process-wide — unlike <see cref="MusicBrainzProvider"/> itself
/// (scoped, one instance per DI scope/request), a per-instance timestamp would let concurrent
/// scoped instances (e.g. two Jellyfin item refreshes racing during a library scan) each pace
/// against their own clock and jointly exceed the limit anyway.
/// </summary>
public sealed class MusicBrainzRateLimiter
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequest;
            if (elapsed < MinInterval)
                await Task.Delay(MinInterval - elapsed, ct);
            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
