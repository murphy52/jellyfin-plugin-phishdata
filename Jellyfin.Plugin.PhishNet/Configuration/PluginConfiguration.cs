using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PhishNet.Configuration;

/// <summary>
/// Plugin configuration class.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ApiKey = string.Empty;
        PreferOfficialReleases = false;
        IncludeJamCharts = true;
        IncludeReviews = true;
        MaxReviews = Constants.DefaultMaxReviews;
        CacheDurationHours = Constants.DefaultCacheDurationHours;
        EnableDebugLogging = false;
        DisableLocalCache = false;
        ShowRatingInTitle = false;
        IncludeSetlistInDescription = true;
    }

    /// <summary>
    /// Gets or sets the Phish.net API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to prefer official releases over audience recordings.
    /// </summary>
    public bool PreferOfficialReleases { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include jam chart data.
    /// </summary>
    public bool IncludeJamCharts { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include community reviews.
    /// </summary>
    public bool IncludeReviews { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of reviews to fetch per show.
    /// </summary>
    public int MaxReviews { get; set; }

    /// <summary>
    /// Gets or sets the cache duration in hours.
    /// </summary>
    public int CacheDurationHours { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable debug logging.
    /// </summary>
    public bool EnableDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to disable local caching.
    /// </summary>
    public bool DisableLocalCache { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show rating in the title.
    /// </summary>
    public bool ShowRatingInTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include setlist in the description.
    /// </summary>
    public bool IncludeSetlistInDescription { get; set; }
}