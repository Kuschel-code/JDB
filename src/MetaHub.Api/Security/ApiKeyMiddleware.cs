using System.Security.Cryptography;
using System.Text;

namespace MetaHub.Api.Security;

/// <summary>
/// Minimal API-key gate for the standalone server. When <c>MetaHub:ApiKey</c> is configured,
/// every request under <c>/api</c> (including <c>/api/admin</c>) must carry a matching
/// <c>X-Api-Key</c> header — the same header the Jellyfin plugin already sends in remote mode.
/// <para>
/// <c>/health</c> stays open for orchestration probes. When no key is configured the API stays
/// open (localhost/dev) but <see cref="Program"/> logs a loud startup warning, so an exposed
/// instance is never silently unauthenticated.
/// </para>
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        var key = config["MetaHub:ApiKey"];
        _expectedKey = string.IsNullOrWhiteSpace(key) ? null : Encoding.UTF8.GetBytes(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expectedKey is null)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !KeyMatches(provided.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key." });
            return;
        }

        await _next(context);
    }

    private bool KeyMatches(string candidate)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), _expectedKey);
}
