using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PhishNet.Services
{
    /// <summary>
    /// Service for managing Phish multi-night run collections.
    /// Creates and manages BoxSet collections for shows that are part of multi-night runs.
    /// </summary>
    public class PhishCollectionService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly ILogger<PhishCollectionService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishCollectionService"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="collectionManager">The collection manager.</param>
        /// <param name="logger">The logger.</param>
        public PhishCollectionService(ILibraryManager libraryManager, ICollectionManager collectionManager, ILogger<PhishCollectionService> logger)
        {
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _logger = logger;
        }

        /// <summary>
        /// Finds an existing collection for the specified city and year.
        /// </summary>
        /// <param name="cityName">The city name.</param>
        /// <param name="year">The year.</param>
        /// <returns>The existing BoxSet collection, or null if not found.</returns>
        public Task<BoxSet?> FindExistingCollectionAsync(string cityName, int year)
        {
            try
            {
                var expectedName = GetCollectionName(cityName, year);
                _logger.LogDebug("Looking for existing collection: {CollectionName}", expectedName);

                // Query for all BoxSet items in the library
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Name = expectedName,
                    Recursive = true
                };

                BoxSet? boxSet = null;
                
                // Try the newer API first (Jellyfin 10.11.x), then fall back to older API
                try
                {
                    // Newer API: returns QueryResult
                    var result = _libraryManager.QueryItems(query);
                    boxSet = result.Items.OfType<BoxSet>().FirstOrDefault();
                }
                catch (MissingMethodException)
                {
                    // Fallback to older API: returns List directly
                    try
                    {
                        var method = _libraryManager.GetType().GetMethod("GetItemList", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            null, new[] { typeof(InternalItemsQuery) }, null);
                        
                        if (method != null)
                        {
                            var items = method.Invoke(_libraryManager, new object[] { query }) as List<BaseItem>;
                            boxSet = items?.OfType<BoxSet>().FirstOrDefault();
                        }
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogDebug(fallbackEx, "Fallback method also failed, trying alternative approach");
                        // Last resort: use reflection to find any method that works
                        boxSet = null;
                    }
                }

                if (boxSet != null)
                {
                    _logger.LogInformation("Found existing collection: {CollectionName} (ID: {CollectionId})", 
                        boxSet.Name, boxSet.Id);
                }

                return Task.FromResult(boxSet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding existing collection for {City} {Year}", cityName, year);
                return Task.FromResult<BoxSet?>(null);
            }
        }

        /// <summary>
        /// Creates a new collection for the specified city and year.
        /// </summary>
        /// <param name="cityName">The city name.</param>
        /// <param name="year">The year.</param>
        /// <returns>The created BoxSet collection.</returns>
        public async Task<BoxSet> CreateCollectionAsync(string cityName, int year)
        {
            try
            {
                var collectionName = GetCollectionName(cityName, year);
                _logger.LogInformation("Creating new collection: {CollectionName}", collectionName);

                // Use the proper collection manager to create the collection
                var boxSet = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
                {
                    Name = collectionName,
                    ParentId = null, // Root level collection
                    IsLocked = false
                });

                // Set additional metadata after creation
                boxSet.Overview = $"Multi-night Phish run in {cityName} during {year}";
                boxSet.Genres = new[] { "Concert", "Live Music" };
                boxSet.Tags = new[] { "Phish", "Multi-Night Run", cityName, year.ToString() };
                
                // Update with the metadata
                await _libraryManager.UpdateItemAsync(boxSet, boxSet.GetParent(), 
                    ItemUpdateType.MetadataEdit, cancellationToken: default);

                _logger.LogInformation("Successfully created collection: {CollectionName} (ID: {CollectionId})", 
                    boxSet.Name, boxSet.Id);

                return boxSet;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating collection for {City} {Year}", cityName, year);
                throw;
            }
        }

        /// <summary>
        /// Adds a movie to the specified collection.
        /// </summary>
        /// <param name="collection">The collection to add to.</param>
        /// <param name="movie">The movie to add.</param>
        /// <returns>True if the movie was added, false if it was already in the collection.</returns>
        public async Task<bool> AddToCollectionAsync(BoxSet collection, Movie movie)
        {
            try
            {
                // Check if the movie has a valid ID - if not, it hasn't been saved to the library yet
                if (movie.Id == Guid.Empty)
                {
                    _logger.LogWarning("Cannot add movie {MovieName} to collection {CollectionName} - movie has empty GUID (not yet saved to library). This is expected during initial metadata refresh.", 
                        movie.Name, collection.Name);
                    return false;
                }

                // Check if movie is already in the collection
                if (collection.ContainsLinkedChildByItemId(movie.Id))
                {
                    _logger.LogDebug("Movie {MovieName} already in collection {CollectionName}", 
                        movie.Name, collection.Name);
                    return false;
                }

                _logger.LogInformation("Adding movie {MovieName} (ID: {MovieId}) to collection {CollectionName} (ID: {CollectionId})", 
                    movie.Name, movie.Id, collection.Name, collection.Id);

                // Use the proper collection manager to add the item
                await _collectionManager.AddToCollectionAsync(collection.Id, new[] { movie.Id });

                // Verify the movie was actually added by checking again
                // Reload the collection to get the updated state
                // Extract city name from collection name (format: "Phish CityName Year")
                var cityName = ExtractCityFromCollectionName(collection.Name);
                var year = collection.ProductionYear ?? DateTime.Now.Year;
                var updatedCollection = await FindExistingCollectionAsync(cityName, year);
                    
                if (updatedCollection != null && updatedCollection.ContainsLinkedChildByItemId(movie.Id))
                {
                    _logger.LogInformation("Successfully verified movie {MovieName} was added to collection {CollectionName}", 
                        movie.Name, collection.Name);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Movie {MovieName} was not found in collection {CollectionName} after add operation - this may indicate a Jellyfin API issue", 
                        movie.Name, collection.Name);
                        
                    // Try alternative approach: refresh the collection
                    try
                    {
                        await _libraryManager.UpdateItemAsync(collection, collection.GetParent(), 
                            ItemUpdateType.MetadataEdit, cancellationToken: default);
                        _logger.LogInformation("Refreshed collection {CollectionName} metadata after add operation", collection.Name);
                    }
                    catch (Exception refreshEx)
                    {
                        _logger.LogWarning(refreshEx, "Failed to refresh collection {CollectionName} after add operation", collection.Name);
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding movie {MovieName} to collection {CollectionName}", 
                    movie.Name, collection.Name);
                return false;
            }
        }

        /// <summary>
        /// Helper method to safely query items across different Jellyfin versions.
        /// </summary>
        private List<BaseItem> SafeGetItemList(InternalItemsQuery query)
        {
            try
            {
                // Try newer API first (Jellyfin 10.11.x)
                var result = _libraryManager.QueryItems(query);
                return result.Items.ToList();
            }
            catch (MissingMethodException)
            {
                // Fallback for older API
                try
                {
                    var method = _libraryManager.GetType().GetMethod("GetItemList", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, new[] { typeof(InternalItemsQuery) }, null);
                    
                    if (method != null)
                    {
                        var result = method.Invoke(_libraryManager, new object[] { query }) as List<BaseItem>;
                        return result ?? new List<BaseItem>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query items using reflection");
                }
                return new List<BaseItem>();
            }
        }

        /// <summary>
        /// Checks if a movie is already in any collection.
        /// </summary>
        /// <param name="movie">The movie to check.</param>
        /// <returns>True if the movie is in a collection, false otherwise.</returns>
        public Task<bool> IsMovieInAnyCollectionAsync(Movie movie)
        {
            try
            {
                // Query for all BoxSet items that might contain this movie
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Recursive = true
                };

                var collections = SafeGetItemList(query).OfType<BoxSet>();

                foreach (var collection in collections)
                {
                    if (collection.ContainsLinkedChildByItemId(movie.Id))
                    {
                        _logger.LogDebug("Movie {MovieName} found in collection {CollectionName}", 
                            movie.Name, collection.Name);
                        return Task.FromResult(true);
                    }
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if movie {MovieName} is in any collection", movie.Name);
                return Task.FromResult(false); // Assume not in collection on error
            }
        }

        /// <summary>
        /// Finds other movies from the same multi-night run.
        /// </summary>
        /// <param name="cityName">The city name.</param>
        /// <param name="year">The year.</param>
        /// <param name="runDates">The dates of all shows in the run.</param>
        /// <param name="excludeMovieId">Movie ID to exclude from results.</param>
        /// <returns>List of movies from the same run.</returns>
        public Task<List<Movie>> FindMoviesFromSameRunAsync(string cityName, int year, 
            List<DateTime> runDates, Guid excludeMovieId)
        {
            try
            {
                _logger.LogDebug("Looking for other movies from {City} {Year} run with {Count} dates", 
                    cityName, year, runDates.Count);

                var movies = new List<Movie>();

                // Query for all movies in the library
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    Recursive = true,
                    Years = new[] { year }
                };

                var allMovies = SafeGetItemList(query).OfType<Movie>();

                // Filter movies that match our run criteria
                foreach (var movie in allMovies)
                {
                    if (movie.Id == excludeMovieId)
                        continue;

                    // Check if this movie's premiere date matches any of our run dates
                    if (movie.PremiereDate.HasValue && 
                        runDates.Any(date => date.Date == movie.PremiereDate.Value.Date))
                    {
                        // Additional check: movie name/overview should contain city name (basic filtering)
                        if (movie.Name?.Contains(cityName, StringComparison.OrdinalIgnoreCase) == true ||
                            movie.Overview?.Contains(cityName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            movies.Add(movie);
                            _logger.LogDebug("Found matching movie: {MovieName} ({Date})", 
                                movie.Name, movie.PremiereDate?.ToString("yyyy-MM-dd"));
                        }
                    }
                }

                _logger.LogInformation("Found {Count} other movies from {City} {Year} run", 
                    movies.Count, cityName, year);

                return Task.FromResult(movies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding movies from same run {City} {Year}", cityName, year);
                return Task.FromResult(new List<Movie>());
            }
        }

        /// <summary>
        /// Gets the standard collection name format.
        /// </summary>
        /// <param name="cityName">The city name.</param>
        /// <param name="year">The year.</param>
        /// <returns>The formatted collection name.</returns>
        private static string GetCollectionName(string cityName, int year)
        {
            return $"Phish {cityName} {year}";
        }

        /// <summary>
        /// Extracts the city name from a collection name in the format "Phish CityName Year".
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <returns>The extracted city name.</returns>
        private static string ExtractCityFromCollectionName(string collectionName)
        {
            if (string.IsNullOrEmpty(collectionName) || !collectionName.StartsWith("Phish "))
            {
                return string.Empty;
            }

            // Remove "Phish " prefix
            var withoutPrefix = collectionName.Substring(6);
            
            // Find the last space followed by a 4-digit year
            var parts = withoutPrefix.Split(' ');
            if (parts.Length < 2)
            {
                return withoutPrefix;
            }

            // The last part should be the year, everything else is the city name
            var lastPart = parts[^1];
            if (lastPart.Length == 4 && int.TryParse(lastPart, out _))
            {
                // Join all parts except the last one (year)
                return string.Join(" ", parts[..^1]);
            }

            // If no year found, return the whole string
            return withoutPrefix;
        }

        /// <summary>
        /// Processes collection logic for a movie that's part of a multi-night run.
        /// This is the main entry point for collection management.
        /// </summary>
        /// <param name="movie">The movie to process.</param>
        /// <param name="cityName">The city name.</param>
        /// <param name="year">The year.</param>
        /// <param name="runDates">The dates of all shows in the run.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ProcessMultiNightRunCollectionAsync(Movie movie, string cityName, int year, 
            List<DateTime> runDates)
        {
            try
            {
                _logger.LogInformation("Processing collection for movie {MovieName} in {City} {Year} run", 
                    movie.Name, cityName, year);

                // Check if an existing collection already exists
                var existingCollection = await FindExistingCollectionAsync(cityName, year);
                
                if (existingCollection != null)
                {
                    // Add this movie to the existing collection
                    await AddToCollectionAsync(existingCollection, movie);
                    return;
                }

                // No existing collection - check if there are other movies from this run
                var otherMovies = await FindMoviesFromSameRunAsync(cityName, year, runDates, movie.Id);
                
                if (otherMovies.Count > 0)
                {
                    // We have multiple movies from the run - create a collection
                    _logger.LogInformation("Creating collection for {City} {Year} with {Count} movies", 
                        cityName, year, otherMovies.Count + 1);

                    var collection = await CreateCollectionAsync(cityName, year);
                    
                    // Add the current movie
                    await AddToCollectionAsync(collection, movie);
                    
                    // Add all other movies from the run
                    foreach (var otherMovie in otherMovies)
                    {
                        await AddToCollectionAsync(collection, otherMovie);
                    }
                }
                else
                {
                    _logger.LogDebug("Only one movie found for {City} {Year} run - no collection needed yet", 
                        cityName, year);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing multi-night run collection for {MovieName}", movie.Name);
            }
        }
    }
}