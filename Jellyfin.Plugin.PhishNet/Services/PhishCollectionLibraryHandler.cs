using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.PhishNet.Parsers;

namespace Jellyfin.Plugin.PhishNet.Services
{
    /// <summary>
    /// Handles library events to process Phish collections after movies are saved to the database.
    /// This ensures movies have valid IDs before attempting to add them to collections.
    /// </summary>
    public class PhishCollectionLibraryHandler : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly PhishCollectionService _collectionService;
        private readonly ILogger<PhishCollectionLibraryHandler> _logger;
        private readonly PhishFileNameParser _filenameParser;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishCollectionLibraryHandler"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="collectionService">The collection service.</param>
        /// <param name="logger">The logger.</param>
        public PhishCollectionLibraryHandler(
            ILibraryManager libraryManager,
            PhishCollectionService collectionService,
            ILogger<PhishCollectionLibraryHandler> logger)
        {
            _libraryManager = libraryManager;
            _collectionService = collectionService;
            _logger = logger;
            
            var parseLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhishFileNameParser>();
            _filenameParser = new PhishFileNameParser(parseLogger);
            
            // Subscribe to library events
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
        }

        /// <summary>
        /// Handles the ItemAdded event from the library manager.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if (_disposed || e.Item is not Movie movie)
                return;

            await ProcessMovieForCollections(movie);
        }

        /// <summary>
        /// Handles the ItemUpdated event from the library manager.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void OnItemUpdated(object? sender, ItemChangeEventArgs e)
        {
            if (_disposed || e.Item is not Movie movie)
                return;

            // Only process if this looks like a Phish movie that might need collection processing
            if (IsPhishMovie(movie))
            {
                await ProcessMovieForCollections(movie);
            }
        }

        /// <summary>
        /// Processes a movie for potential collection inclusion.
        /// </summary>
        /// <param name="movie">The movie to process.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessMovieForCollections(Movie movie)
        {
            try
            {
                if (movie.Id == Guid.Empty)
                {
                    _logger.LogDebug("EVENT DEBUG: Skipping collection processing for {MovieName} - no valid ID yet", movie.Name);
                    return;
                }

                _logger.LogInformation("EVENT DEBUG: Processing movie {MovieName} with ID {MovieId}", movie.Name, movie.Id);

                // Check if this movie has stored collection metadata from the metadata provider
                var cityProviderId = movie.ProviderIds?.GetValueOrDefault("PhishCollectionCity");
                var yearProviderId = movie.ProviderIds?.GetValueOrDefault("PhishCollectionYear");
                var dayNumberProviderId = movie.ProviderIds?.GetValueOrDefault("PhishCollectionDayNumber");
                var dateProviderId = movie.ProviderIds?.GetValueOrDefault("PhishCollectionDate");

                _logger.LogInformation("EVENT DEBUG: Collection metadata - City: {City}, Year: {Year}, Day: {Day}, Date: {Date}", 
                    cityProviderId, yearProviderId, dayNumberProviderId, dateProviderId);

                if (string.IsNullOrEmpty(cityProviderId) || string.IsNullOrEmpty(yearProviderId) || string.IsNullOrEmpty(dayNumberProviderId))
                {
                    _logger.LogDebug("EVENT DEBUG: Movie {MovieName} has no collection metadata, skipping", movie.Name);
                    return;
                }

                if (!int.TryParse(yearProviderId, out var year) || !int.TryParse(dayNumberProviderId, out var dayNumber) || !DateTime.TryParse(dateProviderId, out var showDate))
                {
                    _logger.LogWarning("EVENT DEBUG: Invalid collection metadata for movie {MovieName}", movie.Name);
                    return;
                }

                _logger.LogInformation("EVENT DEBUG: Processing collection for Phish movie {MovieName} (ID: {MovieId}) - {City} {Year} Day {Day}", 
                    movie.Name, movie.Id, cityProviderId, year, dayNumber);

                // Create run dates based on the day number (simple 2-night run)
                var runDates = new List<DateTime>
                {
                    showDate.AddDays(-dayNumber + 1),
                    showDate.AddDays(-dayNumber + 2)
                };

                _logger.LogInformation("EVENT DEBUG: Using run dates: {RunDates}", string.Join(", ", runDates.Select(d => d.ToString("yyyy-MM-dd"))));

                // Process collection for this movie
                await _collectionService.ProcessMultiNightRunCollectionAsync(movie, cityProviderId, year, runDates);
                
                _logger.LogInformation("EVENT DEBUG: Successfully completed collection processing for {MovieName}", movie.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EVENT DEBUG: Error processing post-save collection for movie {MovieName}", movie.Name);
            }
        }

        /// <summary>
        /// Determines if a movie is likely a Phish movie based on simple heuristics.
        /// </summary>
        /// <param name="movie">The movie to check.</param>
        /// <returns>True if this looks like a Phish movie.</returns>
        private static bool IsPhishMovie(Movie movie)
        {
            var name = movie.Name?.ToLower() ?? string.Empty;
            var path = movie.Path?.ToLower() ?? string.Empty;

            return name.Contains("phish") || 
                   path.Contains("phish") || 
                   name.StartsWith("ph") ||
                   movie.Genres?.Contains("Concert") == true;
        }

        /// <summary>
        /// Determines run information based on parse results.
        /// This is a simplified version - in a real implementation you might query an API.
        /// </summary>
        /// <param name="parseResult">The parse result.</param>
        /// <returns>Run information.</returns>
        private static RunInfo DetermineRunInfo(PhishShowParseResult parseResult)
        {
            // For now, assume any show with a day number > 1 is part of a multi-night run
            // In a real implementation, you'd query the API to get the actual run dates
            var dayNumber = parseResult.DayNumber ?? 0;
            var runInfo = new RunInfo
            {
                IsPartOfRun = dayNumber > 0,
                NightNumber = dayNumber,
                TotalNights = dayNumber, // Simplified - would need API call for actual total
                RunDates = new List<DateTime>()
            };

            if (parseResult.ShowDate.HasValue && runInfo.IsPartOfRun)
            {
                // Simplified: just add a few dates around the show date based on day number
                var baseDate = parseResult.ShowDate.Value;
                for (int i = 0; i < runInfo.TotalNights; i++)
                {
                    runInfo.RunDates.Add(baseDate.AddDays(i - dayNumber + 1));
                }
            }

            return runInfo;
        }

        /// <summary>
        /// Disposes the handler and unsubscribes from events.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemUpdated -= OnItemUpdated;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Information about a multi-night run at a venue.
    /// Duplicate of the one in PhishNetMovieProvider - should be moved to shared location.
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
}