using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaHub.Jellyfin.Configuration;

namespace MetaHub.Jellyfin.Api;

/// <summary>
/// Thin client for the MetaHub API. All Jellyfin providers go through here so the plugin only
/// ever talks to the user's MetaHub instance (central caching), never external services.
/// </summary>
public class MetaHubApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MetaHubApiClient(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(MetaHubApiClient));
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, Config.RequestTimeoutSeconds));
        if (!string.IsNullOrWhiteSpace(Config.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", Config.ApiKey);
        return client;
    }

    private static string BaseUrl => Config.ApiBaseUrl.TrimEnd('/');

    /// <summary>Resolves a work by an external id (e.g. source="tmdb"), with optional language.</summary>
    public async Task<WorkDto?> LookupAsync(string source, string id, string? lang, CancellationToken ct)
    {
        var client = CreateClient();
        var url = $"{BaseUrl}/api/lookup?source={Uri.EscapeDataString(source)}&id={Uri.EscapeDataString(id)}";
        if (!string.IsNullOrWhiteSpace(lang))
            url += $"&lang={Uri.EscapeDataString(lang)}";
        try
        {
            var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<WorkDto>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<WorkDto?> GetWorkAsync(Guid id, CancellationToken ct)
    {
        var client = CreateClient();
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/api/work/{id}", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<WorkDto>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<EpisodeDto>> GetEpisodesAsync(Guid workId, CancellationToken ct)
    {
        var client = CreateClient();
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/api/series/{workId}/episodes", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<EpisodeDto>();
            return await response.Content.ReadFromJsonAsync<List<EpisodeDto>>(JsonOptions, ct).ConfigureAwait(false)
                   ?? new List<EpisodeDto>();
        }
        catch (Exception)
        {
            return Array.Empty<EpisodeDto>();
        }
    }

    public async Task<IReadOnlyList<ImageDto>> GetImagesAsync(Guid workId, CancellationToken ct)
    {
        var client = CreateClient();
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/api/work/{workId}/images", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<ImageDto>();
            return await response.Content.ReadFromJsonAsync<List<ImageDto>>(JsonOptions, ct).ConfigureAwait(false)
                   ?? new List<ImageDto>();
        }
        catch (Exception)
        {
            return Array.Empty<ImageDto>();
        }
    }
}

public class WorkDto
{
    public Guid Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string CanonicalTitle { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Overview { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<ExternalIdDto> ExternalIds { get; set; } = new();
    public List<PersonDto> People { get; set; } = new();
}

/// <summary>A cast/crew member of a work (actor, voice actor, director, author, …).</summary>
public class PersonDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>MetaHub CreditRole name, e.g. "Actor", "VoiceActor", "Director".</summary>
    public string Role { get; set; } = string.Empty;
    public string? Character { get; set; }
    public string? ImageUrl { get; set; }
    public int Order { get; set; }
}

public class ExternalIdDto
{
    public string Source { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ImageDto
{
    public string Type { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Source { get; set; }
    public double Score { get; set; }
}
