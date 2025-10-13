using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Web;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Services
{
    /// <summary>
    /// Service for fetching images from external APIs.
    /// </summary>
    public class ExternalImageService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ExternalImageService> _logger;

        public ExternalImageService(IHttpClientFactory httpClientFactory, ILogger<ExternalImageService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Gets venue images from Wikipedia API.
        /// </summary>
        public async Task<List<RemoteImageInfo>> GetWikipediaImagesAsync(string venueName, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                
                // Step 1: Search for the Wikipedia page
                var searchUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(venueName)}";
                
                var response = await httpClient.GetAsync(searchUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Wikipedia search failed for venue: {Venue}", venueName);
                    return images;
                }

                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchResult = JsonSerializer.Deserialize<WikipediaPageSummary>(jsonContent);
                
                if (searchResult?.Thumbnail?.Source != null)
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = searchResult.Thumbnail.Source,
                        Type = ImageType.Primary,
                        ProviderName = "Wikipedia",
                        Width = searchResult.Thumbnail.Width,
                        Height = searchResult.Thumbnail.Height,
                        Language = "en"
                    });
                    
                    _logger.LogDebug("Found Wikipedia image for {Venue}: {Url}", venueName, searchResult.Thumbnail.Source);
                }
                
                // Step 2: Try to get additional images from the page
                await GetWikipediaPageImagesAsync(httpClient, venueName, images, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Wikipedia images for venue: {Venue}", venueName);
            }

            return images;
        }

        /// <summary>
        /// Gets venue images from Unsplash API (requires API key).
        /// </summary>
        public async Task<List<RemoteImageInfo>> GetUnsplashImagesAsync(string venueName, string? city = null, CancellationToken cancellationToken = default)
        {
            var images = new List<RemoteImageInfo>();
            
            // Note: This would require an Unsplash API key in plugin configuration
            var unsplashApiKey = Plugin.Instance?.Configuration?.UnsplashApiKey;
            if (string.IsNullOrEmpty(unsplashApiKey))
            {
                _logger.LogDebug("Unsplash API key not configured");
                return images;
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Client-ID {unsplashApiKey}");
                
                // Build search query
                var searchQuery = venueName;
                if (!string.IsNullOrEmpty(city))
                {
                    searchQuery += $" {city}";
                }
                
                var searchUrl = $"https://api.unsplash.com/search/photos?query={Uri.EscapeDataString(searchQuery)}&per_page=5&orientation=landscape";
                
                var response = await httpClient.GetAsync(searchUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Unsplash search failed for venue: {Venue}", venueName);
                    return images;
                }

                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchResult = JsonSerializer.Deserialize<UnsplashSearchResult>(jsonContent);
                
                if (searchResult?.Results != null)
                {
                    foreach (var photo in searchResult.Results)
                    {
                        if (photo.Urls?.Regular != null)
                        {
                            images.Add(new RemoteImageInfo
                            {
                                Url = photo.Urls.Regular,
                                Type = ImageType.Backdrop,
                                ProviderName = "Unsplash",
                                Width = photo.Width,
                                Height = photo.Height,
                                Language = "en"
                            });
                        }
                    }
                    
                    _logger.LogDebug("Found {Count} Unsplash images for {Venue}", images.Count, venueName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Unsplash images for venue: {Venue}", venueName);
            }

            return images;
        }

        /// <summary>
        /// Gets venue images from Google Places API (requires API key).
        /// </summary>
        public async Task<List<RemoteImageInfo>> GetGooglePlacesImagesAsync(string venueName, string? city = null, string? state = null, CancellationToken cancellationToken = default)
        {
            var images = new List<RemoteImageInfo>();
            
            var googleApiKey = Plugin.Instance?.Configuration?.GooglePlacesApiKey;
            if (string.IsNullOrEmpty(googleApiKey))
            {
                _logger.LogDebug("Google Places API key not configured");
                return images;
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                
                // Step 1: Search for the place to get Place ID
                var searchQuery = venueName;
                if (!string.IsNullOrEmpty(city))
                {
                    searchQuery += $" {city}";
                }
                if (!string.IsNullOrEmpty(state))
                {
                    searchQuery += $" {state}";
                }
                
                var findPlaceUrl = $"https://maps.googleapis.com/maps/api/place/findplacefromtext/json" +
                    $"?input={Uri.EscapeDataString(searchQuery)}" +
                    $"&inputtype=textquery" +
                    $"&fields=place_id,name" +
                    $"&key={googleApiKey}";
                
                var response = await httpClient.GetAsync(findPlaceUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Google Places search failed for venue: {Venue}", venueName);
                    return images;
                }

                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var findPlaceResult = JsonSerializer.Deserialize<GoogleFindPlaceResult>(jsonContent);
                
                if (findPlaceResult?.Candidates?.Count > 0)
                {
                    var placeId = findPlaceResult.Candidates[0].PlaceId;
                    
                    // Step 2: Get place details including photos
                    var detailsUrl = $"https://maps.googleapis.com/maps/api/place/details/json" +
                        $"?place_id={placeId}" +
                        $"&fields=photos" +
                        $"&key={googleApiKey}";
                    
                    var detailsResponse = await httpClient.GetAsync(detailsUrl, cancellationToken);
                    if (detailsResponse.IsSuccessStatusCode)
                    {
                        var detailsContent = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
                        var detailsResult = JsonSerializer.Deserialize<GooglePlaceDetailsResult>(detailsContent);
                        
                        if (detailsResult?.Result?.Photos != null)
                        {
                            foreach (var photo in detailsResult.Result.Photos)
                            {
                                var photoUrl = $"https://maps.googleapis.com/maps/api/place/photo" +
                                    $"?maxwidth=1920" +
                                    $"&photo_reference={photo.PhotoReference}" +
                                    $"&key={googleApiKey}";
                                
                                images.Add(new RemoteImageInfo
                                {
                                    Url = photoUrl,
                                    Type = ImageType.Backdrop,
                                    ProviderName = "Google Places",
                                    Width = photo.Width,
                                    Height = photo.Height,
                                    Language = "en"
                                });
                            }
                        }
                    }
                    
                    _logger.LogDebug("Found {Count} Google Places images for {Venue}", images.Count, venueName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Google Places images for venue: {Venue}", venueName);
            }

            return images;
        }

        private async Task GetWikipediaPageImagesAsync(HttpClient httpClient, string venueName, List<RemoteImageInfo> images, CancellationToken cancellationToken)
        {
            try
            {
                // Get additional images from the Wikipedia page
                var pageUrl = $"https://en.wikipedia.org/api/rest_v1/page/media-list/{Uri.EscapeDataString(venueName)}";
                
                var response = await httpClient.GetAsync(pageUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var mediaResult = JsonSerializer.Deserialize<WikipediaMediaList>(jsonContent);
                    
                    if (mediaResult?.Items != null)
                    {
                        foreach (var item in mediaResult.Items)
                        {
                            if (item.Type == "image" && !string.IsNullOrEmpty(item.SrcSet))
                            {
                                // Parse srcset to get the highest quality image
                                var imageUrl = ParseHighestQualityImageFromSrcSet(item.SrcSet);
                                if (!string.IsNullOrEmpty(imageUrl))
                                {
                                    images.Add(new RemoteImageInfo
                                    {
                                        Url = imageUrl,
                                        Type = ImageType.Backdrop,
                                        ProviderName = "Wikipedia",
                                        Language = "en"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get additional Wikipedia images for: {Venue}", venueName);
            }
        }

        private string? ParseHighestQualityImageFromSrcSet(string srcSet)
        {
            // Parse srcset format: "url1 width1, url2 width2, ..."
            var sources = srcSet.Split(',');
            var highestWidth = 0;
            string? bestUrl = null;
            
            foreach (var source in sources)
            {
                var parts = source.Trim().Split(' ');
                if (parts.Length >= 2)
                {
                    var url = parts[0];
                    var widthStr = parts[1].Replace("w", "");
                    if (int.TryParse(widthStr, out var width) && width > highestWidth)
                    {
                        highestWidth = width;
                        bestUrl = url;
                    }
                }
            }
            
            return bestUrl;
        }
    }

    #region API Response Models

    public class WikipediaPageSummary
    {
        public string? Title { get; set; }
        public WikipediaThumbnail? Thumbnail { get; set; }
    }

    public class WikipediaThumbnail
    {
        public string? Source { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class WikipediaMediaList
    {
        public List<WikipediaMediaItem>? Items { get; set; }
    }

    public class WikipediaMediaItem
    {
        public string? Type { get; set; }
        public string? SrcSet { get; set; }
    }

    public class UnsplashSearchResult
    {
        public List<UnsplashPhoto>? Results { get; set; }
    }

    public class UnsplashPhoto
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public UnsplashUrls? Urls { get; set; }
    }

    public class UnsplashUrls
    {
        public string? Regular { get; set; }
        public string? Full { get; set; }
    }

    public class GoogleFindPlaceResult
    {
        public List<GooglePlaceCandidate>? Candidates { get; set; }
    }

    public class GooglePlaceCandidate
    {
        public string? PlaceId { get; set; }
        public string? Name { get; set; }
    }

    public class GooglePlaceDetailsResult
    {
        public GooglePlaceDetails? Result { get; set; }
    }

    public class GooglePlaceDetails
    {
        public List<GooglePlacePhoto>? Photos { get; set; }
    }

    public class GooglePlacePhoto
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string? PhotoReference { get; set; }
    }

    #endregion
}