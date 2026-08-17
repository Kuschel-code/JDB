using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MetaHub.Enrichment.Providers;
using MetaHub.Identification.AniDb;
using Polly;

namespace MetaHub.Enrichment;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the enrichment pipeline (M4): AniList + Jikan providers, the merger-based
    /// enrichment service, and resilient HttpClients (retry + backoff, honoring 429/5xx).
    /// </summary>
    public static IServiceCollection AddEnrichment(this IServiceCollection services)
    {
        services.AddOptions<EnrichmentOptions>().BindConfiguration(EnrichmentOptions.SectionName);

        AddResilientClient(services, AniListProvider.HttpClientName);
        AddResilientClient(services, JikanProvider.HttpClientName);
        AddResilientClient(services, KitsuProvider.HttpClientName);
        AddResilientClient(services, ShikimoriProvider.HttpClientName);
        AddResilientClient(services, TmdbProvider.HttpClientName, redactUrl: true);
        AddResilientClient(services, FanArtTvProvider.HttpClientName, redactUrl: true);
        AddResilientClient(services, MusicBrainzProvider.HttpClientName);
        AddResilientClient(services, OpenLibraryProvider.HttpClientName);
        AddResilientClient(services, GoogleBooksProvider.HttpClientName, redactUrl: true);
        AddResilientClient(services, AnnictProvider.HttpClientName);
        // Cap the buffered (compressed) body — the client's own 10 MB cap only bounds the
        // decompressed side, after the download has already been buffered in full. No shared
        // retry: AniDbHttpClient already owns end-to-end throttle/ban/retry semantics via its
        // own gate and ThrottleAsync bookkeeping, which has no visibility into retries an outer
        // policy would run transparently underneath it — stacking one on top risks turning a
        // single transient 5xx into several extra physical requests to the one provider whose
        // abuse detection is tuned to punish exactly that.
        AddResilientClient(services, AniDbHttpClient.HttpClientName, maxResponseBytes: 8 * 1024 * 1024, retryCount: 0);

        // Anime
        services.AddScoped<IMetadataProvider, AniListProvider>();
        services.AddScoped<IMetadataProvider, JikanProvider>();
        services.AddScoped<IMetadataProvider, KitsuProvider>();
        services.AddScoped<IMetadataProvider, ShikimoriProvider>();
        services.AddScoped<IMetadataProvider, AniDbHttpProvider>();
        // Movies / series (and complementary anime)
        services.AddScoped<IMetadataProvider, TmdbProvider>();
        // Artwork (posters/backgrounds/logos) keyed by existing tvdb/tmdb/musicbrainz ids
        services.AddScoped<IMetadataProvider, FanArtTvProvider>();
        // Music
        services.AddScoped<IMetadataProvider, MusicBrainzProvider>();
        // Books
        services.AddScoped<IMetadataProvider, OpenLibraryProvider>();
        services.AddScoped<IMetadataProvider, GoogleBooksProvider>();
        // Japanese anime database (token-gated)
        services.AddScoped<IMetadataProvider, AnnictProvider>();

        // Shared across every caller (not scoped) — see MusicBrainzRateLimiter's doc comment.
        services.AddSingleton<MusicBrainzRateLimiter>();

        services.AddScoped<JikanEpisodeSync>();
        services.AddScoped<AniDbEpisodeSync>();
        services.AddScoped<EnrichmentService>();
        services.AddScoped<EnrichmentRunner>();

        return services;
    }

    private static void AddResilientClient(
        IServiceCollection services, string name, bool redactUrl = false, long? maxResponseBytes = null,
        int retryCount = 4)
    {
        // HttpClient.Timeout wraps the whole Polly pipeline, not one attempt — it must leave
        // room for the backoff delays themselves (2+4+8+16 = 30s across 4 retries) plus every
        // attempt's actual request time, or a slow-but-alive provider gets cut off mid-retry.
        // When that happens the client throws OperationCanceledException indistinguishable at
        // the call site from real cancellation; EnrichmentService/MetaHubBackend now guard
        // against misreading it as such, but a timeout that fits the retry budget in the first
        // place means providers get their full number of attempts instead of a truncated few.
        var backoffBudget = TimeSpan.FromSeconds(Enumerable.Range(1, retryCount).Sum(a => Math.Pow(2, a)));
        var clientBuilder = services.AddHttpClient(name, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<EnrichmentOptions>>().Value;
                client.Timeout = backoffBudget + TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                if (maxResponseBytes is { } cap)
                    client.MaxResponseContentBufferSize = cap;
            });

        // Transient errors (5xx, 408, HttpRequestException) plus 429, retried with backoff.
        if (retryCount > 0)
            clientBuilder.AddTransientHttpErrorPolicy(builder => builder
                .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(retryCount, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

        // TMDB / fanart.tv / Google Books carry the API key in the query string; the default
        // IHttpClientFactory logger logs the request URI at Information, which would leak the key
        // into Jellyfin's log. Drop the request loggers for those clients.
        if (redactUrl)
            clientBuilder.RemoveAllLoggers();
    }
}
