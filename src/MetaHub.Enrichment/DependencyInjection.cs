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
        AddResilientClient(services, AniDbHttpClient.HttpClientName);

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

        services.AddScoped<JikanEpisodeSync>();
        services.AddScoped<AniDbEpisodeSync>();
        services.AddScoped<EnrichmentService>();
        services.AddScoped<EnrichmentRunner>();

        return services;
    }

    private static void AddResilientClient(IServiceCollection services, string name, bool redactUrl = false)
    {
        var clientBuilder = services.AddHttpClient(name, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<EnrichmentOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            // Transient errors (5xx, 408, HttpRequestException) plus 429, retried with backoff.
            .AddTransientHttpErrorPolicy(builder => builder
                .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(4, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

        // TMDB / fanart.tv / Google Books carry the API key in the query string; the default
        // IHttpClientFactory logger logs the request URI at Information, which would leak the key
        // into Jellyfin's log. Drop the request loggers for those clients.
        if (redactUrl)
            clientBuilder.RemoveAllLoggers();
    }
}
