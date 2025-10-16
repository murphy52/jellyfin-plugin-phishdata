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

}