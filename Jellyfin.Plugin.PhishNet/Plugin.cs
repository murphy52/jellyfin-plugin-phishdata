using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PhishNet;

/// <summary>
/// The main plugin class for the Phish.net metadata provider.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IHttpClientFactory httpClientFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => Constants.PluginName;

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(Constants.PluginGuid);

    /// <summary>
    /// Gets the plugin's data folder path.
    /// </summary>
    public new string DataFolderPath
    {
        get
        {
            var path = Path.Combine(
                ApplicationPaths.PluginsPath,
                $"phishnet_{Version.ToString(3)}");

            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>
    /// Creates an HTTP client with proper user agent headers.
    /// </summary>
    /// <returns>An HTTP client configured for Phish.net API calls.</returns>
    public HttpClient GetHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(Name, Version.ToString()));

        return httpClient;
    }

    /// <summary>
    /// Gets the plugin thumbnail image.
    /// </summary>
    /// <returns>The plugin thumbnail stream.</returns>
    public Stream GetThumbImage()
    {
        return GetPluginImageStream();
    }
    
    /// <summary>
    /// Gets the plugin image (alternative method name for compatibility).
    /// </summary>
    /// <returns>The plugin image stream.</returns>
    public Stream GetPluginImage()
    {
        return GetPluginImageStream();
    }
    
    /// <summary>
    /// Gets the plugin logo image.
    /// </summary>
    /// <returns>The plugin logo stream.</returns>
    public Stream GetLogoImage()
    {
        return GetPluginImageStream();
    }
    
    /// <summary>
    /// Internal method to get the plugin image stream.
    /// </summary>
    /// <returns>The plugin image stream.</returns>
    private Stream GetPluginImageStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        // Try thumb.png first (preferred), then logo.png as fallback
        var thumbStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.PhishNet.thumb.png");
        if (thumbStream != null && thumbStream.Length > 0)
        {
            return thumbStream;
        }
        
        var logoStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.PhishNet.logo.png");
        if (logoStream != null && logoStream.Length > 0)
        {
            return logoStream;
        }
        
        // Return empty stream if no images found
        return new MemoryStream();
    }
    
    /// <summary>
    /// Static method to get plugin image - used by some Jellyfin versions.
    /// </summary>
    /// <returns>The plugin image stream.</returns>
    public static Stream GetStaticPluginImageStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        // Try thumb.png first (preferred), then logo.png as fallback
        var thumbStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.PhishNet.thumb.png");
        if (thumbStream != null && thumbStream.Length > 0)
        {
            return thumbStream;
        }
        
        var logoStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.PhishNet.logo.png");
        if (logoStream != null && logoStream.Length > 0)
        {
            return logoStream;
        }
        
        // Return empty stream if no images found
        return new MemoryStream();
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "PhishNetConfigPage",
                EmbeddedResourcePath = "Jellyfin.Plugin.PhishNet.Configuration.configPage.html"
            }
        };
    }
}