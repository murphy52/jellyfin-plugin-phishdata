using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.PhishNet.API.Client;
using Jellyfin.Plugin.PhishNet.Parsers;
using Jellyfin.Plugin.PhishNet.Services;

namespace Jellyfin.Plugin.PhishNet.Providers
{
    /// <summary>
    /// Provides images for Phish concert videos using multiple sources.
    /// Priority: Venue photos > Generic concert images > Placeholder images
    /// </summary>
    public class PhishImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly ILogger<PhishImageProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly PhishFileNameParser _filenameParser;
        private readonly ExternalImageService _externalImageService;
        private readonly ShowPhotoService _showPhotoService;
        private PhishNetApiClient? _apiClient;

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name => "Phish.net Images";

        /// <summary>
        /// Gets the provider order (lower = higher priority).
        /// </summary>
        public int Order => 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishImageProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="httpClientFactory">The HTTP client factory.</param>
        public PhishImageProvider(ILogger<PhishImageProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            var parseLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhishFileNameParser>();
            _filenameParser = new PhishFileNameParser(parseLogger);
            
            var externalLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ExternalImageService>();
            _externalImageService = new ExternalImageService(httpClientFactory, externalLogger);
            
            var showPhotoLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ShowPhotoService>();
            _showPhotoService = new ShowPhotoService(httpClientFactory, showPhotoLogger);
        }

        /// <summary>
        /// Gets the supported image types for this provider.
        /// </summary>
        /// <param name="item">The item to get images for.</param>
        /// <returns>The supported image types.</returns>
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[]
            {
                ImageType.Primary,    // Main poster/artwork
                ImageType.Backdrop,   // Background image
                ImageType.Thumb       // Thumbnail
            };
        }

        /// <summary>
        /// Determines if this provider supports the given item.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>True if the item is supported.</returns>
        public bool Supports(BaseItem item)
        {
            // Only support Movies (Phish concert videos)
            if (item is not Movie movie)
                return false;

            // Check if this looks like a Phish show
            var parseResult = _filenameParser.Parse(movie.Name ?? string.Empty, movie.Path ?? string.Empty);
            return parseResult.Confidence > 0.3;
        }

        /// <summary>
        /// Gets available images for a Phish show.
        /// </summary>
        /// <param name="item">The movie item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Available remote image info.</returns>
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            if (item is not Movie movie)
                return images;

            try
            {
                // Parse the movie name to get show information
                var parseResult = _filenameParser.Parse(movie.Name ?? string.Empty, movie.Path ?? string.Empty);
                
                if (parseResult.Confidence < 0.3 || !parseResult.ShowDate.HasValue)
                {
                    _logger.LogDebug("Low confidence parse for image lookup: {Name}", movie.Name);
                    return images;
                }

                _logger.LogDebug("Searching for images for show: {Date} at {Venue}", 
                    parseResult.ShowDate.Value.ToString("yyyy-MM-dd"), parseResult.Venue);

                // Strategy 1: Show-specific photos (PRIORITY - actual photos from that show!)
                await AddShowSpecificPhotosAsync(images, parseResult, cancellationToken);

                // Strategy 2: Venue-based images
                await AddVenueImagesAsync(images, parseResult, cancellationToken);

                // Strategy 3: Generic Phish concert images
                await AddGenericPhishImagesAsync(images, parseResult, cancellationToken);

                // Strategy 4: Date-based or special event images
                await AddSpecialEventImagesAsync(images, parseResult, cancellationToken);

                _logger.LogInformation("Found {Count} images for {Movie}", images.Count, movie.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image info for {Movie}", movie.Name);
            }

            return images;
        }

        /// <summary>
        /// Gets the image response for a given URL.
        /// </summary>
        /// <param name="url">The image URL.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The HTTP response for the image.</returns>
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            return httpClient.GetAsync(url, cancellationToken);
        }

        /// <summary>
        /// Adds show-specific photos of Phish performing at this actual show.
        /// This is the highest priority - real photos from the actual performance!
        /// </summary>
        private async Task AddShowSpecificPhotosAsync(List<RemoteImageInfo> images, PhishShowParseResult parseResult, CancellationToken cancellationToken)
        {
            try
            {
                if (!parseResult.ShowDate.HasValue)
                    return;

                var showDate = parseResult.ShowDate.Value.ToString("yyyy-MM-dd");
                var venue = parseResult.Venue ?? parseResult.City;
                
                _logger.LogDebug("Searching for show-specific photos for {Date} at {Venue}", showDate, venue);
                
                var showPhotos = await _showPhotoService.GetShowPhotosAsync(showDate, venue, cancellationToken);
                images.AddRange(showPhotos);
                
                if (showPhotos.Count > 0)
                {
                    _logger.LogInformation("Found {Count} show-specific photos for {Date}", showPhotos.Count, showDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get show-specific photos");
            }
        }

        /// <summary>
        /// Adds venue-based images using various venue photo APIs.
        /// </summary>
        private async Task AddVenueImagesAsync(List<RemoteImageInfo> images, PhishShowParseResult parseResult, CancellationToken cancellationToken)
        {
            try
            {
                // Initialize API client to get venue information
                await EnsureApiClientAsync(cancellationToken);
                
                if (_apiClient == null || !parseResult.ShowDate.HasValue)
                    return;

                // Get show and venue data
                var dateString = parseResult.ShowDate.Value.ToString("yyyy-MM-dd");
                var shows = await _apiClient.GetShowsAsync(dateString, cancellationToken);
                var showData = shows?.Count > 0 ? shows[0] : null;
                
                if (showData?.VenueId.HasValue == true)
                {
                    var venueData = await _apiClient.GetVenueAsync(showData.VenueId.Value.ToString(), cancellationToken);
                    
                    if (venueData != null)
                    {
                        await AddVenuePhotosFromApisAsync(images, venueData.Name, venueData.City, venueData.State, cancellationToken);
                    }
                }
                else if (!string.IsNullOrEmpty(parseResult.Venue))
                {
                    // Fall back to parsed venue name
                    await AddVenuePhotosFromApisAsync(images, parseResult.Venue, parseResult.City, parseResult.State, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get venue images");
            }
        }

        /// <summary>
        /// Adds venue photos from external APIs (Google Places, Unsplash, etc.).
        /// </summary>
        private async Task AddVenuePhotosFromApisAsync(List<RemoteImageInfo> images, string venueName, string? city, string? state, CancellationToken cancellationToken)
        {
            // Strategy 1: Well-known venues with predictable image sources
            await AddKnownVenueImagesAsync(images, venueName);

            // Strategy 2: Wikipedia images (free, no API key required)
            var wikipediaImages = await _externalImageService.GetWikipediaImagesAsync(venueName, cancellationToken);
            images.AddRange(wikipediaImages);
            
            // Strategy 3: Unsplash images (requires API key)
            var unsplashImages = await _externalImageService.GetUnsplashImagesAsync(venueName, city, cancellationToken);
            images.AddRange(unsplashImages);
            
            // Strategy 4: Google Places images (requires API key)
            var googleImages = await _externalImageService.GetGooglePlacesImagesAsync(venueName, city, state, cancellationToken);
            images.AddRange(googleImages);
        }

        /// <summary>
        /// Adds images for well-known Phish venues with reliable image sources.
        /// </summary>
        private Task AddKnownVenueImagesAsync(List<RemoteImageInfo> images, string venueName)
        {
            // Dictionary of well-known venues and their image sources
            var knownVenues = new Dictionary<string, VenueImageInfo>
            {
                ["Dick's Sporting Goods Park"] = new VenueImageInfo
                {
                    Primary = "https://example.com/dicks-primary.jpg",
                    Backdrop = "https://example.com/dicks-backdrop.jpg",
                    Description = "Dick's Sporting Goods Park - Commerce City, CO"
                },
                ["Madison Square Garden"] = new VenueImageInfo
                {
                    Primary = "https://example.com/msg-primary.jpg", 
                    Backdrop = "https://example.com/msg-backdrop.jpg",
                    Description = "Madison Square Garden - New York, NY"
                },
                ["Red Rocks Amphitheatre"] = new VenueImageInfo
                {
                    Primary = "https://example.com/redrocks-primary.jpg",
                    Backdrop = "https://example.com/redrocks-backdrop.jpg", 
                    Description = "Red Rocks Amphitheatre - Morrison, CO"
                }
            };

            if (knownVenues.TryGetValue(venueName, out var venueInfo))
            {
                if (!string.IsNullOrEmpty(venueInfo.Primary))
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = venueInfo.Primary,
                        Type = ImageType.Primary,
                        ProviderName = Name,
                        Width = 1000,
                        Height = 1500,
                        Language = "en"
                    });
                }

                if (!string.IsNullOrEmpty(venueInfo.Backdrop))
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = venueInfo.Backdrop,
                        Type = ImageType.Backdrop,
                        ProviderName = Name,
                        Width = 1920,
                        Height = 1080,
                        Language = "en"
                    });
                }
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Adds generic Phish concert images for fallback.
        /// </summary>
        private Task AddGenericPhishImagesAsync(List<RemoteImageInfo> images, PhishShowParseResult parseResult, CancellationToken cancellationToken)
        {
            // Add generic Phish concert images as fallbacks
            var genericImages = new[]
            {
                new RemoteImageInfo
                {
                    Url = "https://example.com/phish-generic-primary.jpg",
                    Type = ImageType.Primary,
                    ProviderName = Name,
                    Width = 1000,
                    Height = 1500,
                    Language = "en"
                },
                new RemoteImageInfo
                {
                    Url = "https://example.com/phish-generic-backdrop.jpg", 
                    Type = ImageType.Backdrop,
                    ProviderName = Name,
                    Width = 1920,
                    Height = 1080,
                    Language = "en"
                }
            };

            images.AddRange(genericImages);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Adds special event or date-specific images.
        /// </summary>
        private Task AddSpecialEventImagesAsync(List<RemoteImageInfo> images, PhishShowParseResult parseResult, CancellationToken cancellationToken)
        {
            if (!parseResult.IsSpecialEvent || string.IsNullOrEmpty(parseResult.ShowType))
                return Task.CompletedTask;

            // Handle special events like Halloween, New Year's Eve, Baker's Dozen, etc.
            var specialEventImages = parseResult.ShowType.ToLowerInvariant() switch
            {
                var x when x.Contains("halloween") => new[]
                {
                    new RemoteImageInfo
                    {
                        Url = "https://example.com/phish-halloween-primary.jpg",
                        Type = ImageType.Primary,
                        ProviderName = Name,
                        Width = 1000,
                        Height = 1500
                    }
                },
                var x when x.Contains("new year") => new[]
                {
                    new RemoteImageInfo
                    {
                        Url = "https://example.com/phish-nye-primary.jpg",
                        Type = ImageType.Primary,
                        ProviderName = Name,
                        Width = 1000,
                        Height = 1500
                    }
                },
                var x when x.Contains("baker's dozen") => new[]
                {
                    new RemoteImageInfo
                    {
                        Url = "https://example.com/phish-bakers-dozen-primary.jpg",
                        Type = ImageType.Primary,
                        ProviderName = Name,
                        Width = 1000,
                        Height = 1500
                    }
                },
                _ => Array.Empty<RemoteImageInfo>()
            };

            images.AddRange(specialEventImages);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ensures the API client is initialized.
        /// </summary>
        private Task EnsureApiClientAsync(CancellationToken cancellationToken)
        {
            if (_apiClient != null)
                return Task.CompletedTask;

            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrEmpty(config.ApiKey))
                return Task.CompletedTask;

            var httpClient = _httpClientFactory.CreateClient();
            var apiLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhishNetApiClient>();
            _apiClient = new PhishNetApiClient(httpClient, apiLogger, config.ApiKey);
            
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Information about venue images.
    /// </summary>
    internal class VenueImageInfo
    {
        public string? Primary { get; set; }
        public string? Backdrop { get; set; }
        public string? Thumb { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}