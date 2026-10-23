using System;
using System.IO;
using System.Net.Http;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Jellyfin.Plugin.PhishNet.Providers;
using Jellyfin.Plugin.PhishNet.Providers.ExternalIds;
using Jellyfin.Plugin.PhishNet.Services;

namespace Jellyfin.Plugin.PhishNet
{
    /// <summary>
    /// Plugin service registrator to ensure all providers are properly registered.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <summary>The named HTTP client for phish.in; it doesn't follow redirects, <see cref="PhishInClient"/> does.</summary>
        public const string PhishInHttpClient = "phishin";

        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Explicitly register the image provider to ensure Jellyfin discovers it
            serviceCollection.AddTransient<PhishImageProvider>();

            // Register collection image provider for default images
            serviceCollection.AddTransient<PhishCollectionImageProvider>();

            // Register external ID provider for Phish.net links
            serviceCollection.AddTransient<PhishNetExternalId>();
            serviceCollection.AddTransient<PhishNetExternalUrlProvider>();

            // Register collection service for multi-night runs
            serviceCollection.AddTransient<PhishCollectionService>();

            // Register library event handler for post-save collection processing
            serviceCollection.AddSingleton<PhishCollectionLibraryHandler>();

            RegisterChapterMarkers(serviceCollection);
        }

        /// <summary>
        /// Chapter markers (issue #26). State lives under <c>DataPath/phishdata</c>, which doesn't move when the
        /// plugin version changes. The scheduled task needs no registration: Jellyfin finds
        /// <see cref="ChapterMarkerTask"/> by scanning the assembly and builds it from this container.
        /// </summary>
        private static void RegisterChapterMarkers(IServiceCollection services)
        {
            services.AddHttpClient(PhishInHttpClient)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                });

            services.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance?.Configuration ?? new PluginConfiguration());
            services.AddSingleton<PhishInPacer>();
            services.AddSingleton(sp => new PhishInClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(PhishInHttpClient),
                sp.GetRequiredService<PhishInPacer>(),
                typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0"));
            services.AddSingleton<IEnvelopeDecoder, EnvelopeDecoder>();
            services.AddSingleton(sp => new TemplateCache(DataFolder(sp, "phishin")));
            services.AddSingleton<IShowAudioSource, PhishInTemplateSource>();
            services.AddSingleton(sp => new ChapterStateStore(DataFolder(sp, "chapters")));
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ChapterMarkerService>(sp, TimeProvider.System));
            services.AddSingleton<IChapterMarkerService>(sp => sp.GetRequiredService<ChapterMarkerService>());
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ChapterQueue>(sp, TimeProvider.System));
            services.AddHostedService(sp => sp.GetRequiredService<ChapterQueue>());
        }

        private static string DataFolder(IServiceProvider services, string name) =>
            Path.Combine(services.GetRequiredService<IApplicationPaths>().DataPath, "phishdata", name);
    }
}
