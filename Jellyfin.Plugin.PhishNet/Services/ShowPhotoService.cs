using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.PhishNet.Services
{
    /// <summary>
    /// Service for fetching show-specific photos of Phish performing.
    /// Priority: Official photos > High-quality fan photos > Social media photos
    /// </summary>
    public class ShowPhotoService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ShowPhotoService> _logger;

        public ShowPhotoService(IHttpClientFactory httpClientFactory, ILogger<ShowPhotoService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Gets photos of Phish performing at a specific show.
        /// </summary>
        public async Task<List<RemoteImageInfo>> GetShowPhotosAsync(string showDate, string? venue = null, CancellationToken cancellationToken = default)
        {
            var photos = new List<RemoteImageInfo>();

            try
            {
                _logger.LogDebug("Searching for photos of Phish performing on {Date} at {Venue}", showDate, venue);

                // Strategy 1: Check Phish.net show page for photos
                await AddPhishNetPhotosAsync(photos, showDate, cancellationToken);

                // Strategy 2: Search social media hashtags
                await AddSocialMediaPhotosAsync(photos, showDate, venue, cancellationToken);

                // Strategy 3: Search fan photo platforms
                await AddFanPhotosAsync(photos, showDate, venue, cancellationToken);

                // Strategy 4: Check LivePhish for official artwork
                await AddLivePhishPhotosAsync(photos, showDate, cancellationToken);

                // Filter and rank photos by quality
                photos = FilterAndRankPhotos(photos);

                _logger.LogInformation("Found {Count} show-specific photos for {Date}", photos.Count, showDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting show photos for {Date}", showDate);
            }

            return photos;
        }

        /// <summary>
        /// Scrapes Phish.net show page for any embedded photos or photo gallery links.
        /// </summary>
        private async Task AddPhishNetPhotosAsync(List<RemoteImageInfo> photos, string showDate, CancellationToken cancellationToken)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var showUrl = $"https://phish.net/setlists/{showDate}.html";
                
                var response = await httpClient.GetAsync(showUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Could not fetch Phish.net show page for {Date}", showDate);
                    return;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // Look for photo gallery links, embedded images, etc.
                var photoUrls = ExtractPhotoUrlsFromHtml(html);
                
                foreach (var photoUrl in photoUrls)
                {
                    photos.Add(new RemoteImageInfo
                    {
                        Url = photoUrl,
                        Type = ImageType.Primary,
                        ProviderName = "Phish.net",
                        Language = "en"
                    });
                }

                if (photoUrls.Any())
                {
                    _logger.LogDebug("Found {Count} photos on Phish.net page for {Date}", photoUrls.Count, showDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get photos from Phish.net for {Date}", showDate);
            }
        }

        /// <summary>
        /// Searches social media platforms for show-specific photos using hashtags and date filters.
        /// </summary>
        private async Task AddSocialMediaPhotosAsync(List<RemoteImageInfo> photos, string showDate, string? venue, CancellationToken cancellationToken)
        {
            try
            {
                // Generate hashtags for the specific show
                var dateHash = showDate.Replace("-", ""); // 20240830
                var hashtags = new List<string>
                {
                    $"phish{dateHash}",
                    "phishshow",
                    "phan"
                };

                if (!string.IsNullOrEmpty(venue))
                {
                    var venueHash = venue.Replace(" ", "").Replace("'", "").ToLower();
                    hashtags.Add($"phish{venueHash}");
                }

                // Search Instagram (requires API key)
                await SearchInstagramHashtagsAsync(photos, hashtags, showDate, cancellationToken);

                // Search Twitter/X (requires API key)
                await SearchTwitterHashtagsAsync(photos, hashtags, showDate, cancellationToken);

                _logger.LogDebug("Completed social media search for {Date}", showDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get social media photos for {Date}", showDate);
            }
        }

        /// <summary>
        /// Searches fan photography platforms like Flickr, Reddit, etc.
        /// </summary>
        private async Task AddFanPhotosAsync(List<RemoteImageInfo> photos, string showDate, string? venue, CancellationToken cancellationToken)
        {
            try
            {
                // Search Flickr Creative Commons
                await SearchFlickrAsync(photos, showDate, venue, cancellationToken);

                // Search Reddit r/phish
                await SearchRedditAsync(photos, showDate, cancellationToken);

                _logger.LogDebug("Completed fan photo search for {Date}", showDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get fan photos for {Date}", showDate);
            }
        }

        /// <summary>
        /// Searches LivePhish.com for official show artwork/photos.
        /// </summary>
        private Task AddLivePhishPhotosAsync(List<RemoteImageInfo> photos, string showDate, CancellationToken cancellationToken)
        {
            try
            {
                // This would require scraping LivePhish or using an API if available
                var httpClient = _httpClientFactory.CreateClient();
                
                // Search for LivePhish release with this date
                var searchUrl = $"https://livephish.com/search?q={showDate}";
                
                // Implementation would depend on LivePhish site structure
                _logger.LogDebug("Searching LivePhish for {Date}", showDate);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get LivePhish photos for {Date}", showDate);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Searches Instagram hashtags for show photos (requires Instagram API key).
        /// </summary>
        private Task SearchInstagramHashtagsAsync(List<RemoteImageInfo> photos, List<string> hashtags, string showDate, CancellationToken cancellationToken)
        {
            var instagramApiKey = Plugin.Instance?.Configuration?.InstagramApiKey;
            if (string.IsNullOrEmpty(instagramApiKey))
            {
                _logger.LogDebug("Instagram API key not configured");
                return Task.CompletedTask;
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                
                foreach (var hashtag in hashtags)
                {
                    // Instagram Basic Display API search
                    var searchUrl = $"https://graph.instagram.com/ig_hashtag_search?user_id={{user-id}}&q={hashtag}&access_token={instagramApiKey}";
                    
                    // Implementation would follow Instagram API documentation
                    _logger.LogDebug("Searching Instagram hashtag: #{Hashtag}", hashtag);
                }
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search Instagram for {Date}", showDate);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Searches Twitter/X for show photos (requires Twitter API key).
        /// </summary>
        private Task SearchTwitterHashtagsAsync(List<RemoteImageInfo> photos, List<string> hashtags, string showDate, CancellationToken cancellationToken)
        {
            var twitterApiKey = Plugin.Instance?.Configuration?.TwitterApiKey;
            if (string.IsNullOrEmpty(twitterApiKey))
            {
                _logger.LogDebug("Twitter API key not configured");
                return Task.CompletedTask;
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {twitterApiKey}");
                
                // Build search query
                var query = string.Join(" OR ", hashtags.Select(h => $"#{h}")) + " filter:images";
                var searchUrl = $"https://api.twitter.com/2/tweets/search/recent?query={Uri.EscapeDataString(query)}&tweet.fields=attachments&media.fields=url&expansions=attachments.media_keys";
                
                _logger.LogDebug("Searching Twitter for photos with query: {Query}", query);
                
                // Implementation would follow Twitter API v2 documentation
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search Twitter for {Date}", showDate);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Searches Flickr for Creative Commons show photos.
        /// </summary>
        private Task SearchFlickrAsync(List<RemoteImageInfo> photos, string showDate, string? venue, CancellationToken cancellationToken)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                
                // Flickr API search
                var tags = $"phish,{showDate}";
                if (!string.IsNullOrEmpty(venue))
                {
                    tags += $",{venue}";
                }
                
                var apiKey = "your-flickr-api-key"; // Would need to be configured
                var searchUrl = $"https://api.flickr.com/services/rest/?method=flickr.photos.search&api_key={apiKey}&tags={tags}&license=1,2,3,4,5,6,7&format=json&nojsoncallback=1";
                
                _logger.LogDebug("Searching Flickr for photos tagged: {Tags}", tags);
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search Flickr for {Date}", showDate);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Searches Reddit r/phish for show photos.
        /// </summary>
        private async Task SearchRedditAsync(List<RemoteImageInfo> photos, string showDate, CancellationToken cancellationToken)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-PhishNet-Plugin/1.0");
                
                // Reddit search API
                var searchUrl = $"https://www.reddit.com/r/phish/search.json?q={showDate}&sort=new&restrict_sr=on&t=all";
                
                var response = await httpClient.GetAsync(searchUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    // Parse Reddit response for image posts
                    _logger.LogDebug("Searching Reddit r/phish for {Date}", showDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search Reddit for {Date}", showDate);
            }
        }

        /// <summary>
        /// Extracts photo URLs from HTML content.
        /// </summary>
        private List<string> ExtractPhotoUrlsFromHtml(string html)
        {
            var photoUrls = new List<string>();
            
            try
            {
                // Look for common photo patterns
                var patterns = new[]
                {
                    @"<img[^>]+src=[""']([^""']+\.(?:jpg|jpeg|png|gif))[""'][^>]*>",
                    @"href=[""']([^""']+\.(?:jpg|jpeg|png|gif))[""']",
                    @"url\([""']?([^""')\s]+\.(?:jpg|jpeg|png|gif))[""']?\)"
                };

                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            var url = match.Groups[1].Value;
                            if (IsValidPhotoUrl(url))
                            {
                                photoUrls.Add(url);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract photo URLs from HTML");
            }

            return photoUrls.Distinct().ToList();
        }

        /// <summary>
        /// Validates if a URL appears to be a valid photo.
        /// </summary>
        private bool IsValidPhotoUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            // Must be a valid URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            
            // Must be an image extension
            var path = uri.AbsolutePath.ToLower();
            if (!path.EndsWith(".jpg") && !path.EndsWith(".jpeg") && 
                !path.EndsWith(".png") && !path.EndsWith(".gif")) return false;
            
            // Exclude common non-photo images
            var excludePatterns = new[] { "logo", "icon", "avatar", "thumb", "button" };
            if (excludePatterns.Any(pattern => path.Contains(pattern))) return false;
            
            return true;
        }

        /// <summary>
        /// Filters and ranks photos by quality and relevance.
        /// </summary>
        private List<RemoteImageInfo> FilterAndRankPhotos(List<RemoteImageInfo> photos)
        {
            return photos
                .Where(p => !string.IsNullOrEmpty(p.Url))
                .GroupBy(p => p.Url)
                .Select(g => g.First()) // Remove duplicates
                .OrderBy(p => GetPhotoQualityScore(p))
                .Take(10) // Limit to top 10 photos
                .ToList();
        }

        /// <summary>
        /// Calculates a quality score for photo ranking.
        /// </summary>
        private int GetPhotoQualityScore(RemoteImageInfo photo)
        {
            var score = 0;
            
            // Prefer official sources
            if (photo.ProviderName == "Phish.net") score += 100;
            if (photo.ProviderName == "LivePhish") score += 90;
            if (photo.ProviderName == "Flickr") score += 50;
            
            // Prefer higher resolution
            if (photo.Width > 1200) score += 30;
            else if (photo.Width > 800) score += 20;
            else if (photo.Width > 400) score += 10;
            
            return score;
        }
    }
}