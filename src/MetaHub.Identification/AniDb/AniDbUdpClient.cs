using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MetaHub.Identification.AniDb;

/// <summary>
/// Minimal AniDB UDP API client for file identification. Implements AUTH, the FILE command
/// (lookup by size + ED2K), and LOGOUT, with conservative rate limiting and a single
/// reusable session. Designed to be used defensively: one request at a time, results cached
/// by the caller, never queried twice for the same file.
/// </summary>
public sealed class AniDbUdpClient : IAniDbClient, IDisposable
{
    // FILE fmask: aid + eid + gid (byte 1 bits 0x40|0x20|0x10).
    private const string FMask = "7000000000";
    // FILE amask: anime type (b1 0x10) + english name (b2 0x20) + epno (b3 0x80).
    private const string AMask = "10208000";

    private readonly AniDbOptions _options;
    private readonly ILogger<AniDbUdpClient> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UdpClient? _udp;
    private string? _session;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public AniDbUdpClient(IOptions<AniDbOptions> options, ILogger<AniDbUdpClient> log)
    {
        _options = options.Value;
        _log = log;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task<AniDbFileResult?> LookupFileAsync(long sizeBytes, string ed2kHash, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureAuthenticatedAsync(ct);

            var command = $"FILE size={sizeBytes}&ed2k={ed2kHash}&fmask={FMask}&amask={AMask}&s={_session}";
            var response = await SendAsync(command, ct);

            // 220 = FILE found, 320 = no such file.
            if (response.StartsWith("320"))
            {
                _log.LogInformation("AniDB: no file for ed2k {Ed2k} size {Size}", ed2kHash, sizeBytes);
                return null;
            }
            if (!response.StartsWith("220"))
            {
                _log.LogWarning("AniDB FILE unexpected response: {Response}", FirstLine(response));
                return null;
            }

            return ParseFileResponse(response);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_session is not null)
            return;

        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("AniDB credentials are not configured.");

        _udp ??= new UdpClient(_options.Host, _options.Port);

        var command =
            $"AUTH user={_options.Username}&pass={_options.Password}" +
            $"&protover=3&client={_options.ClientName}&clientver={_options.ClientVersion}&enc=UTF8";

        var response = await SendAsync(command, ct);

        // 200/201 LOGIN ACCEPTED — session key is the first token after the code.
        if (response.StartsWith("200") || response.StartsWith("201"))
        {
            var parts = FirstLine(response).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                _session = parts[1];
                _log.LogInformation("AniDB: authenticated");
                return;
            }
        }

        throw new InvalidOperationException($"AniDB AUTH failed: {FirstLine(response)}");
    }

    private async Task<string> SendAsync(string command, CancellationToken ct)
    {
        await ThrottleAsync(ct);

        var payload = Encoding.UTF8.GetBytes(command);
        await _udp!.SendAsync(payload, payload.Length);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ReceiveTimeoutSeconds));

        var result = await _udp.ReceiveAsync(timeout.Token);
        return Encoding.UTF8.GetString(result.Buffer);
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var minInterval = TimeSpan.FromSeconds(_options.MinRequestIntervalSeconds);
        var elapsed = DateTimeOffset.UtcNow - _lastRequest;
        if (elapsed < minInterval)
            await Task.Delay(minInterval - elapsed, ct);
        _lastRequest = DateTimeOffset.UtcNow;
    }

    private static AniDbFileResult ParseFileResponse(string response)
    {
        // Format: "220 FILE\n{fid}|{aid}|{eid}|{gid}|{type}|{englishName}|{epno}"
        var dataLine = response.Split('\n', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? string.Empty;
        var f = dataLine.Split('|');

        string At(int i) => i < f.Length ? f[i].Trim() : string.Empty;

        return new AniDbFileResult
        {
            FileId = At(0),
            AnimeId = At(1),
            EpisodeId = At(2),
            GroupId = At(3),
            AnimeType = At(4),
            AnimeTitleEnglish = At(5),
            EpisodeNumber = At(6),
            RawResponse = dataLine
        };
    }

    private static string FirstLine(string s) => s.Split('\n', 2)[0].Trim();

    public void Dispose()
    {
        if (_session is not null && _udp is not null)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes($"LOGOUT s={_session}");
                _udp.Send(bytes, bytes.Length);
            }
            catch
            {
                // best-effort logout
            }
        }

        _udp?.Dispose();
        _gate.Dispose();
    }
}
