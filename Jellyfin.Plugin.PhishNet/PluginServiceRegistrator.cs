using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Jellyfin.Plugin.PhishNet.Providers;
using Jellyfin.Plugin.PhishNet.Providers.ExternalIds;

namespace Jellyfin.Plugin.PhishNet
{
    /// <summary>
    /// Plugin service registrator to ensure all providers are properly registered.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Explicitly register the image provider to ensure Jellyfin discovers it
            serviceCollection.AddTransient<PhishImageProvider>();
            
            // Explicitly register the person provider to ensure Jellyfin discovers it
            serviceCollection.AddTransient<PhishPersonProvider>();
            
            // Register external ID providers for Phish.net links
            serviceCollection.AddTransient<PhishNetExternalId>();
            serviceCollection.AddTransient<PhishNetSetlistExternalId>();
            serviceCollection.AddTransient<PhishNetVenueExternalId>();
            serviceCollection.AddTransient<PhishNetReviewsExternalId>();
        }
    }
}