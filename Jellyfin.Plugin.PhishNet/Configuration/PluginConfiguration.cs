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
    }

    /// <summary>
    /// Gets or sets the Phish.net API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically add multi-night Phish runs to collections.
    /// </summary>
    public bool EnableCollections { get; set; } = true;

    /// <summary>
    /// Gets or sets the Unsplash API key for venue images (optional).
    /// </summary>
    public string? UnsplashApiKey { get; set; }
    
    /// <summary>
    /// Gets or sets the Google Places API key for venue images (optional).
    /// </summary>
    public string? GooglePlacesApiKey { get; set; }
    
    /// <summary>
    /// Gets or sets the Instagram API key for show-specific photos (optional).
    /// </summary>
    public string? InstagramApiKey { get; set; }
    
    /// <summary>
    /// Gets or sets the Twitter API key for show-specific photos (optional).
    /// </summary>
    public string? TwitterApiKey { get; set; }
    
    /// <summary>
    /// Gets or sets the Flickr API key for show-specific photos (optional).
    /// </summary>
    public string? FlickrApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to add chapter markers to Phish shows. Off by default: it reads
    /// whole video files and downloads track audio from phish.in.
    /// </summary>
    public bool EnableChapterMarkers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether new and changed shows get chapters soon after a library scan.
    /// When off, only the scheduled task adds them.
    /// </summary>
    public bool ChapterMarkersOnLibraryChanges { get; set; } = true;

    /// <summary>
    /// Gets or sets the most shows the scheduled task aligns in one run.
    /// </summary>
    public int ChapterMarkersMaxShowsPerRun { get; set; } = 10;

    /// <summary>
    /// Gets or sets the most video data, in GB, the scheduled task reads in one run.
    /// </summary>
    public int ChapterMarkersMaxGbPerRun { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether Jellyfin's generated "Chapter N" markers may be replaced.
    /// Real chapters are never replaced.
    /// </summary>
    public bool ReplaceDummyChapters { get; set; } = true;
}