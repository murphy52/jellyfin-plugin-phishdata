using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Jellyfin.Plugin.PhishNet.Configuration;
using Jellyfin.Plugin.PhishNet.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet;

/// <summary>
/// The main plugin class for the Phish.net metadata provider.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Plugin> _logger;
    private PhishCollectionLibraryHandler? _libraryHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="serviceProvider">Instance of the <see cref="IServiceProvider"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{Plugin}"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Initialize the library event handler
        try
        {
            _libraryHandler = serviceProvider.GetService<PhishCollectionLibraryHandler>();
            if (_libraryHandler != null)
            {
                _logger.LogInformation("PLUGIN DEBUG: Successfully initialized PhishCollectionLibraryHandler");
            }
            else
            {
                _logger.LogWarning("PLUGIN DEBUG: Failed to get PhishCollectionLibraryHandler from service provider");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PLUGIN DEBUG: Error initializing PhishCollectionLibraryHandler");
        }
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
