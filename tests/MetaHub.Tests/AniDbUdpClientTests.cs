using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MetaHub.Identification.AniDb;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Drives <see cref="AniDbUdpClient"/> against a loopback stand-in for the AniDB UDP API.
///
/// UDP carries no request/response correlation, so a reply that arrives after its command
/// timed out stays queued on the socket and would be read as the answer to the *next*
/// command — silently stamping one file's anime and episode ids onto a different file.
/// </summary>
public class AniDbUdpClientTests
{
    /// <summary>Loopback UDP server that answers AUTH and lets each test script the FILE replies.</summary>
    private sealed class FakeAniDbServer : IDisposable
    {
        private readonly UdpClient _server;
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<int, string, (string? Reply, TimeSpan Delay)> _onFile;
        private int _fileCommands;

        public int Port { get; }
        public int AuthCount;

        public FakeAniDbServer(Func<int, string, (string? Reply, TimeSpan Delay)> onFile)
        {
            _onFile = onFile;
            _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Port = ((IPEndPoint)_server.Client.LocalEndPoint!).Port;
            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult request;
                try { request = await _server.ReceiveAsync(_cts.Token); }
                catch (OperationCanceledException) { return; }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }

                var text = Encoding.UTF8.GetString(request.Buffer);

                if (text.StartsWith("AUTH", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref AuthCount);
                    await SendAsync("200 s3ss10n LOGIN ACCEPTED", request.RemoteEndPoint);
                    continue;
                }

                if (text.StartsWith("FILE", StringComparison.Ordinal))
                {
                    var (reply, delay) = _onFile(Interlocked.Increment(ref _fileCommands), text);
                    if (reply is null)
                        continue; // never answer

                    // Reply out of band so a delayed answer races the next command, exactly
                    // like a slow AniDB response would.
                    var target = request.RemoteEndPoint;
                    _ = Task.Run(async () =>
                    {
                        if (delay > TimeSpan.Zero)
                            await Task.Delay(delay);
                        await SendAsync(reply, target);
                    });
                }
            }
        }

        private async Task SendAsync(string message, IPEndPoint to)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            try { await _server.SendAsync(bytes, bytes.Length, to); }
            catch (ObjectDisposedException) { /* test finished */ }
            catch (SocketException) { /* test finished */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _server.Dispose();
            _cts.Dispose();
        }
    }

    private static AniDbUdpClient NewClient(int port) =>
        new(Options.Create(new AniDbOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            Username = "u",
            Password = "p",
            MinRequestIntervalSeconds = 0, // keep the test fast; pacing is not under test
            ReceiveTimeoutSeconds = 1
        }), NullLogger<AniDbUdpClient>.Instance);

    [Fact]
    public async Task A_reply_that_arrives_after_its_timeout_is_not_returned_for_the_next_file()
    {
        // First FILE is answered too late to be read; second is answered promptly. The client
        // must return the *second* file's record, never the stale first one.
        using var server = new FakeAniDbServer((n, _) => n == 1
            ? ("220 FILE\n111|1001|2001|3001|TV|Stale Anime|1", TimeSpan.FromMilliseconds(1500))
            : ("220 FILE\n222|1002|2002|3002|TV|Correct Anime|2", TimeSpan.Zero));

        using var client = NewClient(server.Port);

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.LookupFileAsync(100, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));

        // Give the late reply time to land wherever it is going.
        await Task.Delay(1000);

        var second = await client.LookupFileAsync(200, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.NotNull(second);
        Assert.Equal("1002", second!.AnimeId);
        Assert.Equal("Correct Anime", second.AnimeTitleEnglish);
    }

    [Fact]
    public async Task A_timeout_surfaces_as_TimeoutException_not_as_cancellation()
    {
        // A batch scan treats OperationCanceledException as "we are shutting down"; an unanswered
        // AniDB command must not look like that.
        using var server = new FakeAniDbServer((_, _) => (null, TimeSpan.Zero));
        using var client = NewClient(server.Port);

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.LookupFileAsync(100, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    }

    [Fact]
    public async Task Caller_cancellation_still_surfaces_as_cancellation()
    {
        using var server = new FakeAniDbServer((_, _) => (null, TimeSpan.Zero));
        using var client = NewClient(server.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.LookupFileAsync(100, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", cts.Token));
    }

    [Fact]
    public async Task A_successful_lookup_parses_the_file_record()
    {
        using var server = new FakeAniDbServer((_, _) =>
            ("220 FILE\n555|1234|5678|91011|TV Series|Cowboy Bebop|5", TimeSpan.Zero));
        using var client = NewClient(server.Port);

        var result = await client.LookupFileAsync(700_000_000, "cccccccccccccccccccccccccccccccc");

        Assert.NotNull(result);
        Assert.Equal("555", result!.FileId);
        Assert.Equal("1234", result.AnimeId);
        Assert.Equal("5678", result.EpisodeId);
        Assert.Equal("Cowboy Bebop", result.AnimeTitleEnglish);
        Assert.Equal("5", result.EpisodeNumber);
    }

    [Fact]
    public async Task No_such_file_returns_null_without_throwing()
    {
        using var server = new FakeAniDbServer((_, _) => ("320 NO SUCH FILE", TimeSpan.Zero));
        using var client = NewClient(server.Port);

        Assert.Null(await client.LookupFileAsync(1, "dddddddddddddddddddddddddddddddd"));
    }

    [Fact]
    public async Task An_invalid_session_triggers_one_reauthentication()
    {
        // 501 means the session expired: the client re-authenticates and retries once.
        using var server = new FakeAniDbServer((n, _) => n == 1
            ? ("501 LOGIN FIRST", TimeSpan.Zero)
            : ("220 FILE\n777|1|2|3|TV|Recovered|1", TimeSpan.Zero));
        using var client = NewClient(server.Port);

        var result = await client.LookupFileAsync(1, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");

        Assert.NotNull(result);
        Assert.Equal("Recovered", result!.AnimeTitleEnglish);
        Assert.True(server.AuthCount >= 2, "expected a second AUTH after the invalid session");
    }

    [Fact]
    public async Task A_ban_stops_further_requests_until_the_backoff_expires()
    {
        using var server = new FakeAniDbServer((_, _) => ("555 BANNED", TimeSpan.Zero));
        using var client = NewClient(server.Port);

        Assert.Null(await client.LookupFileAsync(1, "ffffffffffffffffffffffffffffffff"));

        // Second call must short-circuit on the ban rather than hit the server again.
        var authsAfterBan = server.AuthCount;
        Assert.Null(await client.LookupFileAsync(2, "00000000000000000000000000000000"));
        Assert.Equal(authsAfterBan, server.AuthCount);
    }

    [Fact]
    public async Task A_disabled_client_never_touches_the_network()
    {
        var client = new AniDbUdpClient(
            Options.Create(new AniDbOptions { Enabled = false }),
            NullLogger<AniDbUdpClient>.Instance);

        Assert.False(client.IsEnabled);
        Assert.Null(await client.LookupFileAsync(1, "11111111111111111111111111111111"));
        client.Dispose();
    }
}
