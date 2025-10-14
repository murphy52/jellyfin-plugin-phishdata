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
                
                if (parseResult.Confidence < 0.25)
                {
                    _logger.LogInformation("Low confidence parse result for {Name} (confidence: {Confidence}), skipping", 
                        info.Name, parseResult.Confidence);
                    return result;
                }

                _logger.LogInformation("Successfully parsed {Name} with confidence {Confidence}: Date={Date}, City={City}, Venue={Venue}, DayNumber={DayNumber}", 
                    info.Name, parseResult.Confidence, parseResult.ShowDate?.ToString("yyyy-MM-dd"), 
                    parseResult.City, parseResult.Venue, parseResult.DayNumber);

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
                

                // Detect if this show is part of a multi-night run
                RunInfo? runInfo = null;
                if (parseResult.ShowDate.HasValue)
                {
                    runInfo = await DetectRunInfoAsync(showData, parseResult.ShowDate.Value, cancellationToken);
                }

                // Populate full metadata
                PopulateMetadataFromApi(result.Item, parseResult, showData, setlistData?.FirstOrDefault(), venueData, runInfo, _apiClient, cancellationToken);

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
        /// <param name="client">The API client for additional data requests.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private void PopulateMetadataFromApi(
            Movie movie, 
            PhishShowParseResult parseResult,
            ShowDto showData, 
            SetlistDto? setlistData, 
            VenueDto? venueData,
            RunInfo? runInfo,
            IPhishNetApiClient client,
            CancellationToken cancellationToken)
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
            

            // Build formatted overview with improved setlist formatting
            var overviewParts = new List<string>();

            // Start with venue name and location (prominent display)
            if (venueData != null && !string.IsNullOrEmpty(venueData.Name))
            {
                overviewParts.Add(venueData.Name.ToUpperInvariant());
                if (!string.IsNullOrEmpty(venueData.City) && !string.IsNullOrEmpty(venueData.State))
                {
                    overviewParts.Add($"{venueData.City}, {venueData.State}");
                }
            }
            else if (locationParts.Count > 0)
            {
                // Fallback to basic location if no venue data
                overviewParts.Add(string.Join(", ", locationParts).ToUpperInvariant());
            }
            
            // Add blank line after venue information
            if (overviewParts.Count > 0)
            {
                overviewParts.Add("");
            }

            // Add properly formatted setlist information 
            if (setlistData?.ParsedSetlist?.Sets != null && setlistData.ParsedSetlist.Sets.Any())
            {
                var setListParts = new List<string>();
                
                foreach (var set in setlistData.ParsedSetlist.Sets)
                {
                    // Build the set string with proper spacing after transitions
                    var setString = new List<string>();
                    for (int i = 0; i < set.Songs.Count; i++)
                    {
                        var song = set.Songs[i];
                        var songText = song.Title;
                        
                        // Add transition mark if not the last song in the set
                        // DisplayTransition already has proper spacing (" > " or ", ")
                        if (i < set.Songs.Count - 1)
                        {
                            songText += song.DisplayTransition;
                        }
                        
                        setString.Add(songText);
                    }
                    
                    // Add the formatted set with proper line breaks between sets
                    setListParts.Add($"{set.SetName}: {string.Join("", setString)}");
                }
                
                // Join sets with double line breaks for proper separation
                overviewParts.Add(string.Join("\n\n", setListParts));
            }
            
            // Collect all footnotes from songs
            var allFootnotes = new List<string>();
            if (setlistData?.ParsedSetlist?.Sets != null)
            {
                foreach (var set in setlistData.ParsedSetlist.Sets)
                {
                    foreach (var song in set.Songs)
                    {
                        if (!string.IsNullOrEmpty(song.Notes))
                        {
                            allFootnotes.Add(CleanText(song.Notes));
                        }
                    }
                }
            }
            
            // Add show notes if available (from ShowDto)
            if (!string.IsNullOrEmpty(showData.ShowNotes))
            {
                overviewParts.Add(""); // Blank line before notes
                overviewParts.Add(CleanText(showData.ShowNotes));
            }
            
            // Add setlist notes if available (from ShowDto)
            if (!string.IsNullOrEmpty(showData.SetlistNotes))
            {
                overviewParts.Add(""); // Blank line before setlist notes
                // Strip HTML if present in setlist notes and clean character encoding
                var cleanNotes = System.Text.RegularExpressions.Regex.Replace(showData.SetlistNotes, "<.*?>", string.Empty);
                overviewParts.Add(CleanText(cleanNotes));
            }
            
            // Add footnotes if any exist
            if (allFootnotes.Any())
            {
                overviewParts.Add(""); // Blank line before footnotes
                overviewParts.Add("Notes:");
                overviewParts.Add("");
                
                for (int i = 0; i < allFootnotes.Count; i++)
                {
                    // Footnotes are already cleaned when added to allFootnotes
                    overviewParts.Add($"[{i + 1}] {allFootnotes[i]}");
                }
            }

            movie.Overview = string.Join("\n", overviewParts);

            // Set hard-coded genres
            movie.Genres = new[] { "Concert", "Live Music" };
            
            // Note: Phish band members are handled by the separate PhishPersonProvider
            // which provides metadata for Trey Anastasio, Mike Gordon, Jon Fishman, and Page McConnell
            // when Jellyfin requests person information

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

            // Set external ID for linking to Phish.net show page
            // Use permalink from setlist data if available, otherwise fall back to date-based URL
            if (setlistData?.Permalink != null && !string.IsNullOrEmpty(setlistData.Permalink))
            {
                // Use the permalink directly - it should be a complete URL
                movie.SetProviderId("PhishNet", setlistData.Permalink);
                _logger.LogDebug("Using permalink for external link: {Permalink}", setlistData.Permalink);
            }
            else
            {
                // Fallback to date-based URL
                var showDateString = showDate.ToString("yyyy-MM-dd");
                movie.SetProviderId("PhishNet", $"https://phish.net/show/{showDateString}");
                _logger.LogDebug("Using fallback date-based URL for external link");
            }
        }
        
        /// <summary>
        /// Cleans text by fixing common character encoding issues from the Phish.net API.
        /// </summary>
        /// <param name="text">The text to clean.</param>
        /// <returns>The cleaned text with proper characters.</returns>
        private static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            
            // Fix common character encoding issues from Phish.net API
            // These include Windows-1252/UTF-8 mojibake seen in API content
            return text
                // Apostrophes and possessives
                .Replace("â’s", "'s")          // "Trey's" becomes "Trey's"
                .Replace("âs", "'s")           // "Halleyâs" -> "Halley's"
                .Replace("â’", "'")            // Other apostrophes
                // Quotes
                .Replace("â“", "\"")           // Opening quotes
                .Replace("â”", "\"")           // Closing quotes  
                // Dashes
                .Replace("â–", "-")           // En dash
                .Replace("â—", "—")           // Em dash
                // Non-breaking spaces appearing as Â or NBSP
                .Replace("Â ", " ")            // NBSP + space to space
                .Replace("Â", " ")             // Lone Â to space
                .Replace("\u00A0", " ")         // Unicode NBSP
                // Collapse multiple spaces
                .Replace("  ", " ")
                .Trim();
        }
    }
}
