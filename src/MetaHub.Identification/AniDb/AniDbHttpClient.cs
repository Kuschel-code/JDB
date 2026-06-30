using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MetaHub.Identification.AniDb;

/// <summary>
/// AniDB HTTP API client for rich anime metadata (titles, episodes, characters, relations).
/// Separate from the UDP client — own client registration, own rate limits, own ban tracking.
/// Responses are gzip-compressed XML. This is a thin transport layer; caching is handled
/// by the enrichment pipeline via <see cref="MetaHub.Domain.Entities.RawPayload"/>.
/// </summary>
public sealed class AniDbHttpClient
{
    public const string HttpClientName = "anidb-http";

    private readonly AniDbOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AniDbHttpClient> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private DateTimeOffset _bannedUntil = DateTimeOffset.MinValue;

    public AniDbHttpClient(
        IOptions<AniDbOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<AniDbHttpClient> log)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <summary>
    /// Fetches the raw anime XML for the given <paramref name="aid"/>.
    /// Returns null on error, ban, or when AniDB is disabled.
    /// Rate-limits and serializes requests through a gate.
    /// </summary>
    public async Task<string?> FetchAnimeXmlAsync(int aid, CancellationToken ct = default)
    {
        if (!_options.Enabled) return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow < _bannedUntil)
            {
                _log.LogWarning("AniDB HTTP: skipping fetch — banned until {Until:u}.", _bannedUntil);
                return null;
            }

            var xml = await FetchGzippedXmlAsync(aid, ct);
            if (xml is null) return null;

            if (AniDbAnimeParser.IsError(xml, out var error))
            {
                HandleError(error);
                return null;
            }

            return xml;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> FetchGzippedXmlAsync(int aid, CancellationToken ct)
    {
        await ThrottleAsync(ct);

        var url = $"{_options.HttpApiUrl}?request=anime" +
                  $"&client={_options.HttpClientName}&clientver={_options.HttpClientVersion}" +
                  $"&protover=1&aid={aid}";

        var client = _httpFactory.CreateClient(HttpClientName);
        try
        {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("AniDB HTTP: {StatusCode} for aid {Aid}.", response.StatusCode, aid);
                return null;
            }

            await using var raw = await response.Content.ReadAsStreamAsync(ct);
            await using var gzip = new GZipStream(raw, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            return await reader.ReadToEndAsync(ct);
        }
        catch (InvalidDataException)
        {
            var response = await client.GetAsync(url, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
    }

    private void HandleError(string error)
    {
        if (error.Contains("Banned", StringComparison.OrdinalIgnoreCase))
        {
            _bannedUntil = DateTimeOffset.UtcNow.Add(_options.BanBackoff);
            _log.LogError("AniDB HTTP: BANNED — backing off until {Until:u}.", _bannedUntil);
        }
        else
        {
            _log.LogWarning("AniDB HTTP error: {Error}", error);
        }
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var minInterval = TimeSpan.FromSeconds(_options.HttpMinRequestIntervalSeconds);
        var elapsed = DateTimeOffset.UtcNow - _lastRequest;
        if (elapsed < minInterval)
            await Task.Delay(minInterval - elapsed, ct);
        _lastRequest = DateTimeOffset.UtcNow;
    }
}
