using System.Diagnostics;
using MetaHub.Enrichment.Providers;
using Xunit;

namespace MetaHub.Tests;

public class MusicBrainzRateLimiterTests
{
    [Fact]
    public async Task Consecutive_calls_are_spaced_at_least_one_second_apart()
    {
        // MusicBrainz's documented limit is ~1 req/s. A concrete regression here is a burst of
        // calls landing back-to-back — exactly what happens with no shared limiter when
        // concurrent Jellyfin item refreshes each create their own scoped MusicBrainzProvider.
        var limiter = new MusicBrainzRateLimiter();
        var sw = Stopwatch.StartNew();

        await limiter.WaitAsync();
        var first = sw.Elapsed;
        await limiter.WaitAsync();
        var second = sw.Elapsed;

        Assert.True(first < TimeSpan.FromMilliseconds(500), "the first call should not itself be throttled");
        Assert.True(second - first >= TimeSpan.FromMilliseconds(1000),
            $"expected >= 1000ms between calls, got {(second - first).TotalMilliseconds}ms");
    }

    [Fact]
    public async Task Concurrent_callers_are_serialized_not_allowed_to_race_the_clock_independently()
    {
        // The bug this guards against: each caller reads "elapsed since last request", and two
        // callers racing the same near-zero elapsed value would both proceed immediately instead
        // of the second one waiting for the first's newly-updated timestamp.
        var limiter = new MusicBrainzRateLimiter();
        var sw = Stopwatch.StartNew();

        await Task.WhenAll(limiter.WaitAsync(), limiter.WaitAsync(), limiter.WaitAsync());

        // Three calls must span at least 2 full intervals (0 -> 1s -> 2s), not complete together.
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(2000),
            $"expected >= 2000ms for 3 serialized calls, got {sw.Elapsed.TotalMilliseconds}ms");
    }
}
