using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Providers
{
    /// <summary>
    /// Provides default images for Phish multi-night run collections (BoxSets).
    /// </summary>
    public class PhishCollectionImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly ILogger<PhishCollectionImageProvider> _logger;

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name => "Phish Collection Images";

        /// <summary>
        /// Gets the provider order (lower = higher priority).
        /// </summary>
        public int Order => 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishCollectionImageProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public PhishCollectionImageProvider(ILogger<PhishCollectionImageProvider> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the supported image types for collections.
        /// </summary>
        /// <param name="item">The item to get images for.</param>
        /// <returns>The supported image types.</returns>
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[]
            {
                ImageType.Primary,   // Poster image for the collection
                ImageType.Backdrop   // Background image
            };
        }

        /// <summary>
        /// Determines if this provider supports the given item.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item is a BoxSet (collection) that looks like a Phish collection.</returns>
        public bool Supports(BaseItem item)
        {
            if (item is not BoxSet boxSet)
                return false;

            // Check if this looks like a Phish collection by name
            var name = boxSet.Name?.ToLower() ?? string.Empty;
            return name.StartsWith("phish");
        }

        /// <summary>
        /// Gets available images for a Phish collection.
        /// </summary>
        /// <param name="item">The collection item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Available remote image info.</returns>
        public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>
            {
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = "phish-collection-poster"
                },
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Backdrop,
                    Url = "phish-collection-backdrop"
                }
            };

            _logger.LogDebug("Providing {Count} images for collection {CollectionName}", images.Count, item.Name);
            return Task.FromResult((IEnumerable<RemoteImageInfo>)images);
        }

        /// <summary>
        /// Gets the image response for a given URL.
        /// </summary>
        /// <param name="url">The image URL (pseudo-URL pointing to embedded resource).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The HTTP response containing the image data.</returns>
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException(nameof(url));
            }

            string resourceName;
            if (url == "phish-collection-poster")
            {
                resourceName = "Jellyfin.Plugin.PhishNet.Resources.collection-poster.png";
            }
            else if (url == "phish-collection-backdrop")
            {
                resourceName = "Jellyfin.Plugin.PhishNet.Resources.collection-backdrop.png";
            }
            else
            {
                throw new ArgumentException($"Unknown image URL: {url}", nameof(url));
            }

            var assembly = Assembly.GetExecutingAssembly();
            var imageStream = assembly.GetManifestResourceStream(resourceName);

            if (imageStream == null)
            {
                _logger.LogWarning("Could not find embedded resource: {ResourceName}", resourceName);
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
            }

            var response = new HttpResponseMessage
            {
                Content = new StreamContent(imageStream)
            };

            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }
}
