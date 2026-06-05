using Microsoft.Extensions.DependencyInjection;
using MetaHub.Ingest.Anime;
using Polly;

namespace MetaHub.Ingest;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the anime ingest pipeline (M2): dataset source, upsert service and runner,
    /// plus a resilient HttpClient with retry + exponential backoff (Polly).
    /// </summary>
    public static IServiceCollection AddAnimeIngest(this IServiceCollection services)
    {
        services.AddOptions<AnimeIngestOptions>()
            .BindConfiguration(AnimeIngestOptions.SectionName);

        services.AddHttpClient(HttpDatasetSource.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<AnimeIngestOptions>>().Value;
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            // Retry transient failures (HttpRequestException, 5xx, 408) with 2s, 4s, 8s, 16s backoff.
            .AddTransientHttpErrorPolicy(builder => builder.WaitAndRetryAsync(
                retryCount: 4,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

        services.AddScoped<IDatasetSource, HttpDatasetSource>();
        services.AddScoped<AnimeIngestService>();
        services.AddScoped<AnimeIngestRunner>();

        return services;
    }
}
