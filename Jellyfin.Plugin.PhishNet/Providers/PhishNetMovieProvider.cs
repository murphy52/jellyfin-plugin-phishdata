using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Jellyfin.Plugin.PhishNet.API.Models;
using Jellyfin.Plugin.PhishNet.Parsers;

namespace Jellyfin.Plugin.PhishNet.Providers
{
    /// <summary>
    /// Information about a multi-night run at a venue.
    /// </summary>
    public class RunInfo
    {
        /// <summary>
        /// Gets or sets whether this show is part of a multi-night run.
        /// </summary>
        public bool IsPartOfRun { get; set; }

        /// <summary>
        /// Gets or sets the night number within the run (1, 2, 3, etc.).
        /// </summary>
        public int NightNumber { get; set; }

        /// <summary>
        /// Gets or sets the total number of nights in the run.
        /// </summary>
        public int TotalNights { get; set; }

        /// <summary>
        /// Gets or sets the dates of all shows in the run.
        /// </summary>
        public List<DateTime> RunDates { get; set; } = new();

        /// <summary>
        /// Gets the formatted night indicator (e.g., "N1", "N2", "N3").
        /// </summary>
        public string NightIndicator => IsPartOfRun ? $"N{NightNumber}" : string.Empty;
    }

    /// <summary>
    /// Provides metadata for Phish concert videos by parsing filenames and querying the Phish.net API.
    /// This provider is automatically registered by Jellyfin through reflection.
    /// </summary>
    public class PhishNetMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
    {
        private readonly ILogger<PhishNetMovieProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly PhishFileNameParser _filenameParser;
        private PhishNetApiClient? _apiClient;

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name => "Phish.net";

        /// <summary>
        /// Gets the provider order (lower = higher priority).
        /// </summary>
        public int Order => 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishNetMovieProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="httpClientFactory">The HTTP client factory.</param>
        public PhishNetMovieProvider(ILogger<PhishNetMovieProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            var parseLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhishFileNameParser>();
            _filenameParser = new PhishFileNameParser(parseLogger);
        }

        /// <summary>
        /// Gets the supported images for Phish shows.
        /// </summary>
        /// <param name="item">The movie item.</param>
        /// <returns>The supported image types.</returns>
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Backdrop,
                ImageType.Thumb
            };
        }

        /// <summary>
        /// Gets metadata for a Phish concert video.
        /// </summary>
        /// <param name="info">The movie info containing filename and path information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The metadata result.</returns>
        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>
            {
                HasMetadata = false,
                Item = new Movie()
            };

            try
            {
                _logger.LogDebug("Processing movie info for: {Name}", info.Name);

                // Parse the filename to extract show information
                var parseResult = _filenameParser.Parse(info.Name, info.Path);
                
                if (parseResult.Confidence < 0.3)
                {
                    _logger.LogInformation("Low confidence parse result for {Name}, skipping", info.Name);
                    return result;
                }

                _logger.LogDebug("Parsed {Name} with confidence {Confidence}: {Date} at {Venue}", 
                    info.Name, parseResult.Confidence, parseResult.ShowDate?.ToString("yyyy-MM-dd"), parseResult.Venue);

                // Initialize API client if needed
                await EnsureApiClientAsync(cancellationToken);
                if (_apiClient == null)
                {
                    _logger.LogWarning("API client not available, using basic metadata only");
                    
                    // Still create basic metadata from parsed filename
                    PopulateBasicMetadata(result.Item, parseResult, info.Name);
                    result.HasMetadata = true;
                    return result;
                }

                // Query the API for show information
                List<ShowDto>? shows = null;
                if (parseResult.ShowDate.HasValue)
                {
                    var dateString = parseResult.ShowDate.Value.ToString("yyyy-MM-dd");
                    shows = await _apiClient.GetShowsAsync(dateString, cancellationToken);
                }

                var showData = shows?.FirstOrDefault();
                if (showData == null)
                {
                    _logger.LogInformation("No show data found for {Date}", parseResult.ShowDate?.ToString("yyyy-MM-dd"));
                    
                    // Still create basic metadata from parsed filename
                    PopulateBasicMetadata(result.Item, parseResult, info.Name);
                    result.HasMetadata = true;
                    return result;
                }

                // Get additional data - setlist now includes proper transition marks
                List<SetlistDto>? setlistData = null;
                if (parseResult.ShowDate.HasValue)
                {
                    var dateString = parseResult.ShowDate.Value.ToString("yyyy-MM-dd");
                    setlistData = await _apiClient.GetSetlistAsync(dateString, cancellationToken);
                }
                VenueDto? venueData = null;
                if (showData.VenueId.HasValue && showData.VenueId > 0)
                {
                    venueData = await _apiClient.GetVenueAsync(showData.VenueId.Value.ToString(), cancellationToken);
                }
                
                // Get reviews for community rating calculation (if enabled)
                List<ReviewDto>? reviewsData = null;
                var config = Plugin.Instance?.Configuration;
                
                // Log configuration status
                if (config == null)
                {
                    _logger.LogWarning("Plugin configuration is not available, reviews disabled");
                }
                else
                {
                    _logger.LogDebug("Configuration: IncludeReviews={IncludeReviews}, MaxReviews={MaxReviews}", 
                        config.IncludeReviews, config.MaxReviews);
                }
                
                if (parseResult.ShowDate.HasValue && config?.IncludeReviews == true)
                {
                    var dateString = parseResult.ShowDate.Value.ToString("yyyy-MM-dd");
                    var maxReviews = config.MaxReviews > 0 ? config.MaxReviews : 50;
                    _logger.LogInformation("Fetching reviews for {Date} (max: {MaxReviews})", dateString, maxReviews);
                    
                    try
                    {
                        reviewsData = await _apiClient.GetReviewsAsync(dateString, maxReviews, cancellationToken);
                        
                        if (reviewsData != null && reviewsData.Any())
                        {
                            _logger.LogInformation("Successfully fetched {Count} reviews for {Date}", reviewsData.Count, dateString);
                        }
                        else
                        {
                            _logger.LogInformation("No reviews found for {Date}", dateString);
                        }
                    }
                    catch (Exception reviewEx)
                    {
                        _logger.LogError(reviewEx, "Failed to fetch reviews for {Date}", dateString);
                    }
                }
                else if (parseResult.ShowDate.HasValue)
                {
                    var reason = config == null ? "configuration not available" 
                        : config.IncludeReviews ? "unknown reason" 
                        : "reviews disabled in configuration";
                    _logger.LogDebug("Skipping reviews for {Date}: {Reason}", 
                        parseResult.ShowDate.Value.ToString("yyyy-MM-dd"), reason);
                }

                // Detect if this show is part of a multi-night run
                RunInfo? runInfo = null;
                if (parseResult.ShowDate.HasValue)
                {
                    runInfo = await DetectRunInfoAsync(showData, parseResult.ShowDate.Value, cancellationToken);
                }

                // Populate full metadata
                PopulateMetadataFromApiAsync(result.Item, parseResult, showData, setlistData?.FirstOrDefault(), venueData, runInfo, reviewsData);

                result.HasMetadata = true;
                _logger.LogDebug("Successfully populated metadata for {Name}", info.Name);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting metadata for {Name}", info.Name);
            }

            return result;
        }

        /// <summary>
        /// Gets available images for a Phish show.
        /// </summary>
        /// <param name="item">The movie item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The available remote image info.</returns>
        public Task<IEnumerable<RemoteImageInfo>> GetImageInfos(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            // For now, return empty list - we could add venue images or show-specific artwork later
            // This would be where we'd return links to venue photos, poster art, etc.
            
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }

        /// <summary>
        /// Searches for Phish shows based on the provided movie info.
        /// </summary>
        /// <param name="searchInfo">The search information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The search results.</returns>
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            try
            {
                // Parse the search name to extract show information
                var parseResult = _filenameParser.Parse(searchInfo.Name ?? string.Empty, searchInfo.Path);
                
                if (parseResult.Confidence < 0.3 || !parseResult.ShowDate.HasValue)
                {
                    return results;
                }

                // Initialize API client if needed
                await EnsureApiClientAsync(cancellationToken);
                if (_apiClient == null)
                {
                    return results;
                }

                // Search for the show
                var dateString = parseResult.ShowDate.Value.ToString("yyyy-MM-dd");
                var shows = await _apiClient.GetShowsAsync(dateString, cancellationToken);
                var showData = shows?.FirstOrDefault();
                if (showData != null)
                {
                    var showDate = DateTime.Parse(showData.ShowDate);
                    var searchResult = new RemoteSearchResult
                    {
                        Name = $"Phish - {showDate:yyyy-MM-dd}",
                        ProductionYear = showDate.Year,
                        PremiereDate = showDate
                    };

                    if (!string.IsNullOrEmpty(showData.City))
                    {
                        searchResult.Name += $" - {showData.City}";
                        if (!string.IsNullOrEmpty(showData.State))
                        {
                            searchResult.Name += $", {showData.State}";
                        }
                    }

                    searchResult.SetProviderId("PhishNet", $"{showDate:yyyy-MM-dd}");
                    
                    results.Add(searchResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for Phish shows with query: {Query}", searchInfo.Name);
            }

            return results;
        }

        /// <summary>
        /// Gets an image response from a URL.
        /// </summary>
        /// <param name="url">The image URL.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The HTTP response for the image.</returns>
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = GetHttpClient();
            return httpClient.GetAsync(url, cancellationToken);
        }

        /// <summary>
        /// Gets the HTTP client for this provider.
        /// </summary>
        /// <returns>The HTTP client.</returns>
        public HttpClient GetHttpClient()
        {
            return _httpClientFactory.CreateClient();
        }

        /// <summary>
        /// Detects if a show is part of a multi-night run and determines the night number.
        /// </summary>
        /// <param name="currentShow">The current show data.</param>
        /// <param name="showDate">The show date.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Run information for the show.</returns>
        private async Task<RunInfo> DetectRunInfoAsync(ShowDto currentShow, DateTime showDate, CancellationToken cancellationToken)
        {
            var runInfo = new RunInfo();

            try
            {
                // Search for shows within a 7-day window around this show
                var startDate = showDate.AddDays(-7).ToString("yyyy-MM-dd");
                var endDate = showDate.AddDays(7).ToString("yyyy-MM-dd");
                
                var nearbyShows = await _apiClient!.GetShowsAsync(startDate, endDate, cancellationToken);
                if (nearbyShows == null || !nearbyShows.Any())
                {
                    return runInfo; // No run detected
                }

                // Filter to shows at the same venue
                var sameVenueShows = nearbyShows
                    .Where(s => s.VenueId == currentShow.VenueId && s.VenueId.HasValue)
                    .Select(s => new { Show = s, Date = DateTime.Parse(s.ShowDate) })
                    .OrderBy(s => s.Date)
                    .ToList();

                if (sameVenueShows.Count < 2)
                {
                    return runInfo; // Single show, not a run
                }

                // Find consecutive nights
                var consecutiveRuns = FindConsecutiveRuns(sameVenueShows.Select(s => s.Date).ToList());
                
                // Find which run this show belongs to
                foreach (var run in consecutiveRuns)
                {
                    if (run.Contains(showDate))
                    {
                        runInfo.IsPartOfRun = run.Count > 1;
                        runInfo.NightNumber = run.IndexOf(showDate) + 1;
                        runInfo.TotalNights = run.Count;
                        runInfo.RunDates = run;
                        break;
                    }
                }

                _logger.LogDebug("Run detection for {Date}: {IsRun}, Night {Night} of {Total}", 
                    showDate.ToString("yyyy-MM-dd"), runInfo.IsPartOfRun, runInfo.NightNumber, runInfo.TotalNights);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect run info for {Date}", showDate.ToString("yyyy-MM-dd"));
            }

            return runInfo;
        }

        /// <summary>
        /// Finds groups of consecutive dates that represent multi-night runs.
        /// </summary>
        /// <param name="dates">The list of show dates to analyze.</param>
        /// <returns>A list of consecutive date runs.</returns>
        private static List<List<DateTime>> FindConsecutiveRuns(List<DateTime> dates)
        {
            var runs = new List<List<DateTime>>();
            if (!dates.Any()) return runs;

            var sortedDates = dates.OrderBy(d => d).ToList();
            var currentRun = new List<DateTime> { sortedDates[0] };

            for (int i = 1; i < sortedDates.Count; i++)
            {
                var currentDate = sortedDates[i];
                var previousDate = sortedDates[i - 1];

                // Check if this date is consecutive (next day)
                if ((currentDate - previousDate).TotalDays == 1)
                {
                    currentRun.Add(currentDate);
                }
                else
                {
                    // End of current run, start new run
                    runs.Add(currentRun);
                    currentRun = new List<DateTime> { currentDate };
                }
            }

            // Add the last run
            runs.Add(currentRun);

            return runs;
        }

        /// <summary>
        /// Ensures the API client is initialized with configuration.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async Task EnsureApiClientAsync(CancellationToken cancellationToken)
        {
            if (_apiClient != null)
            {
                return;
            }

            // Get plugin configuration
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrEmpty(config.ApiKey))
            {
                _logger.LogWarning("Phish.net API key not configured");
                return;
            }

            _logger.LogDebug("Initializing Phish.net API client");
            var httpClient = _httpClientFactory.CreateClient();
            var apiLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhishNetApiClient>();
            _apiClient = new PhishNetApiClient(httpClient, apiLogger, config.ApiKey);

            // Test the connection
            var isConnected = await _apiClient.TestConnectionAsync(cancellationToken);
            if (!isConnected)
            {
                _logger.LogWarning("Failed to connect to Phish.net API");
                _apiClient = null;
            }
        }

        /// <summary>
        /// Populates basic metadata from the parsed filename when API data is unavailable.
        /// </summary>
        /// <param name="movie">The movie item to populate.</param>
        /// <param name="parseResult">The filename parse result.</param>
        /// <param name="originalName">The original filename.</param>
        private static void PopulateBasicMetadata(Movie movie, PhishShowParseResult parseResult, string originalName)
        {
            // Set basic title using consistent format: [band] [city] [date]
            if (parseResult.ShowDate.HasValue)
            {
                var titleParts = new List<string> { "Phish" };
                
                // Add city if available
                if (!string.IsNullOrEmpty(parseResult.City))
                {
                    titleParts.Add(parseResult.City);
                }
                
                // Add formatted date
                titleParts.Add(parseResult.ShowDate.Value.ToString("M-d-yyyy"));
                
                movie.Name = string.Join(" ", titleParts);

                movie.PremiereDate = parseResult.ShowDate.Value;
                movie.ProductionYear = parseResult.ShowDate.Value.Year;
            }
            else
            {
                movie.Name = $"Phish - {originalName}";
            }

            // Add show type to title if it's a special event
            if (parseResult.IsSpecialEvent && !string.IsNullOrEmpty(parseResult.ShowType))
            {
                movie.Name += $" ({parseResult.ShowType})";
            }

            // Set basic metadata
            if (parseResult.ShowDate.HasValue)
            {
                var location = "";
                if (!string.IsNullOrEmpty(parseResult.Venue))
                {
                    location = $" at {parseResult.Venue}";
                }
                else if (!string.IsNullOrEmpty(parseResult.City))
                {
                    location = $" in {parseResult.City}";
                    if (!string.IsNullOrEmpty(parseResult.State))
                    {
                        location += $", {parseResult.State}";
                    }
                }
                
                movie.Overview = $"Phish concert performed on {parseResult.ShowDate.Value:MMMM d, yyyy}{location}";
                
                // Set Release Date and Year
                movie.DateCreated = parseResult.ShowDate.Value;
            }
            else
            {
                movie.Overview = $"Phish concert video: {originalName}";
            }
            
            // Set hard-coded genres
            movie.Genres = new[] { "Concert", "Live Music" };
            
            // TODO: Add Phish band members as People - need to implement via PersonProvider
            // Band members: Trey Anastasio, Mike Gordon, Jon Fishman, Page McConnell
            
            // Add tags for organization (including "Phish")
            movie.Tags = new[] { "Phish", "Concert", "Live Music" }.ToArray();
            
            if (parseResult.IsSpecialEvent)
            {
                movie.Tags = movie.Tags.Concat(new[] { "Special Event" }).ToArray();
            }

            if (!string.IsNullOrEmpty(parseResult.ShowType))
            {
                movie.Tags = movie.Tags.Concat(new[] { parseResult.ShowType }).ToArray();
            }
        }

        /// <summary>
        /// Populates comprehensive metadata using API data.
        /// </summary>
        /// <param name="movie">The movie item to populate.</param>
        /// <param name="parseResult">The filename parse result.</param>
        /// <param name="showData">The show data from the API.</param>
        /// <param name="setlistData">The setlist data from the API with transition marks.</param>
        /// <param name="venueData">The venue data from the API.</param>
        /// <param name="runInfo">The multi-night run information.</param>
        /// <param name="reviewsData">The reviews data from the API for community rating.</param>
        private void PopulateMetadataFromApiAsync(
            Movie movie, 
            PhishShowParseResult parseResult,
            ShowDto showData, 
            SetlistDto? setlistData, 
            VenueDto? venueData,
            RunInfo? runInfo,
            List<ReviewDto>? reviewsData)
        {
            // Set title with official show information using format: [night] [band] [city] [date]
            var showDate = DateTime.Parse(showData.ShowDate);
            
            var titleParts = new List<string>();
            
            // Add night indicator for multi-night runs (N1, N2, N3, etc.)
            if (runInfo != null && runInfo.IsPartOfRun)
            {
                titleParts.Add(runInfo.NightIndicator);
            }
            
            // Add band name
            titleParts.Add("Phish");
            
            // Add city (prefer venue city, fall back to show city)
            var city = venueData?.City ?? showData.City;
            if (!string.IsNullOrEmpty(city))
            {
                titleParts.Add(city);
            }
            
            // Add formatted date (M-d-yyyy format)
            titleParts.Add(showDate.ToString("M-d-yyyy"));
            
            // Combine title parts
            movie.Name = string.Join(" ", titleParts);
            
            // Add special event designation if applicable
            if (parseResult.IsSpecialEvent && !string.IsNullOrEmpty(parseResult.ShowType))
            {
                movie.Name += $" ({parseResult.ShowType})";
            }
            
            // Build location parts for overview
            var locationParts = new List<string>();
            if (venueData != null && !string.IsNullOrEmpty(venueData.Name))
            {
                locationParts.Add(venueData.Name);
            }
            
            if (!string.IsNullOrEmpty(showData.City))
            {
                locationParts.Add(showData.City);
            }
            
            if (!string.IsNullOrEmpty(showData.State))
            {
                locationParts.Add(showData.State);
            }

            // Set dates
            movie.PremiereDate = showDate;
            movie.ProductionYear = showDate.Year;
            movie.DateCreated = showDate; // Release Date
            
            // Calculate community rating from reviews
            _logger.LogDebug("Processing reviews for community rating: {ReviewCount} reviews available", 
                reviewsData?.Count ?? 0);
            
            if (reviewsData != null && reviewsData.Any())
            {
                // Log all ratings found
                foreach (var review in reviewsData)
                {
                    _logger.LogTrace("Review {ReviewId}: Raw rating '{Rating}', Parsed: {ParsedRating}", 
                        review.ReviewId, review.Rating, review.ParsedRating);
                }
                
                var ratingsWithValues = reviewsData
                    .Where(r => r.ParsedRating.HasValue && r.ParsedRating.Value > 0)
                    .Select(r => r.ParsedRating!.Value)
                    .ToList();
                    
                _logger.LogDebug("Found {ValidRatings} valid ratings out of {TotalReviews} reviews", 
                    ratingsWithValues.Count, reviewsData.Count);
                    
                if (ratingsWithValues.Any())
                {
                    var averageRating = ratingsWithValues.Average();
                    // Phish.net uses 1-5 scale, Jellyfin uses 1-10, so multiply by 2
                    var communityRating = (float)(averageRating * 2.0);
                    movie.CommunityRating = communityRating;
                    
                    _logger.LogInformation("Set community rating for {ShowDate}: {Rating}/10 (avg of {Count} reviews: {AvgOriginal}/5)", 
                        showData.ShowDate, communityRating.ToString("F1"), ratingsWithValues.Count, averageRating.ToString("F1"));
                }
                else
                {
                    _logger.LogDebug("No valid numeric ratings found in reviews for {ShowDate}", showData.ShowDate);
                }
            }
            else
            {
                _logger.LogDebug("No reviews available for community rating calculation for {ShowDate}", showData.ShowDate);
            }

            // Build comprehensive overview with setlist at the top
            var overviewParts = new List<string>();

            // Add setlist information first - this is what fans want to see immediately
            if (setlistData?.ParsedSetlist?.Sets != null && setlistData.ParsedSetlist.Sets.Any())
            {
                overviewParts.Add($"Setlist ({setlistData.ParsedSetlist.TotalSongs} songs):");
                
                foreach (var set in setlistData.ParsedSetlist.Sets)
                {
                    // Build the set string with proper transition marks
                    var setString = new List<string>();
                    for (int i = 0; i < set.Songs.Count; i++)
                    {
                        var song = set.Songs[i];
                        var songText = song.Title;
                        
                        // Add transition mark if not the last song in the set
                        if (i < set.Songs.Count - 1)
                        {
                            songText += song.DisplayTransition.TrimEnd(); // Remove trailing space
                        }
                        
                        setString.Add(songText);
                    }
                    
                    overviewParts.Add($"{set.SetName}: {string.Join("", setString)}");
                }
                
                // Add separator before show details
                overviewParts.Add("");
            }

            // Add show details
            var showDetailsLine = $"Phish concert performed on {showDate:MMMM d, yyyy}";
            if (locationParts.Count > 0)
            {
                showDetailsLine += $" at {string.Join(", ", locationParts)}";
            }
            overviewParts.Add(showDetailsLine);

            // Add venue information if different from location
            if (venueData != null && !string.IsNullOrEmpty(venueData.City))
            {
                var venueInfo = new List<string> { $"Venue: {venueData.Name}" };
                
                // Only add location if it's not already in the show details
                if (!locationParts.Contains(venueData.Name))
                {
                    venueInfo.Add($"Location: {venueData.City}, {venueData.State}");
                }
                
                overviewParts.Add("");
                overviewParts.AddRange(venueInfo);
            }

            movie.Overview = string.Join(" ", overviewParts);

            // Set hard-coded genres
            movie.Genres = new[] { "Concert", "Live Music" };
            
            // TODO: Add Phish band members as People - need to implement via PersonProvider
            // Band members: Trey Anastasio, Mike Gordon, Jon Fishman, Page McConnell

            // Set genres/tags (including "Phish")
            var tags = new List<string> { "Phish", "Concert", "Live Music", "Jam Band" };
            
            if (!string.IsNullOrEmpty(showData.City))
            {
                tags.Add(showData.City);
            }
            
            if (!string.IsNullOrEmpty(showData.State))
            {
                tags.Add(showData.State);
            }
            
            if (parseResult.IsSpecialEvent)
            {
                tags.Add("Special Event");
                
                if (!string.IsNullOrEmpty(parseResult.ShowType))
                {
                    tags.Add(parseResult.ShowType);
                }
            }

            // Add venue as a tag
            if (venueData != null && !string.IsNullOrEmpty(venueData.Name))
            {
                tags.Add(venueData.Name);
            }

            movie.Tags = tags.Distinct().ToArray();

            // Set external IDs for linking
            movie.SetProviderId("PhishNet", $"{showDate:yyyy-MM-dd}");
            
            if (venueData?.VenueId > 0)
            {
                movie.SetProviderId("PhishNetVenue", venueData.VenueId.ToString());
            }
        }
    }
}