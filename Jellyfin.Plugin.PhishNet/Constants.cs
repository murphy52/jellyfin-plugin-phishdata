namespace Jellyfin.Plugin.PhishNet;

/// <summary>
/// Plugin constants.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Plugin name.
    /// </summary>
    public const string PluginName = "Phish Data";

    /// <summary>
    /// Plugin GUID.
    /// </summary>
    public const string PluginGuid = "8a4d0e85-6f3c-4e5a-9b2c-3d7f8e9a1b4c";

    /// <summary>
    /// Phish.net API base URL.
    /// </summary>
    public const string PhishNetApiBaseUrl = "https://api.phish.net/v5/";

    /// <summary>
    /// Default cache duration in hours.
    /// </summary>
    public const int DefaultCacheDurationHours = 24;


    /// <summary>
    /// Phish artist name for API calls.
    /// </summary>
    public const string PhishArtistName = "phish";

    /// <summary>
    /// HTTP client timeout in seconds.
    /// </summary>
    public const int HttpClientTimeoutSeconds = 30;
}