namespace MetaHub.Identification.AniDb;

/// <summary>
/// Configuration for the AniDB UDP API client. AniDB requires a registered client
/// (name + version) and a user account. Limits are strict, so the client is conservative
/// by default and must be explicitly enabled.
/// </summary>
public class AniDbOptions
{
    public const string SectionName = "AniDb";

    /// <summary>Master switch. When false, AniDB lookups are skipped entirely.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "api.anidb.net";
    public int Port { get; set; } = 9000;

    /// <summary>Registered AniDB UDP client name.</summary>
    public string ClientName { get; set; } = "metahub";

    /// <summary>Registered AniDB UDP client version (integer).</summary>
    public int ClientVersion { get; set; } = 1;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Minimum delay between packets. AniDB asks for ~2s short-term and 4s sustained;
    /// default to the safe value.
    /// </summary>
    public int MinRequestIntervalSeconds { get; set; } = 4;

    /// <summary>How long to wait for a UDP reply before giving up.</summary>
    public int ReceiveTimeoutSeconds { get; set; } = 20;
}
