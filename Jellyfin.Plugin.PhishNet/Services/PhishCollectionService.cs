using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.PhishNet.Services
{
    /// <summary>
    /// Service for managing Phish multi-night run collections.
    /// Creates and manages BoxSet collections for shows that are part of multi-night runs.
    /// </summary>
    public class PhishCollectionService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<PhishCollectionService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishCollectionService"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        public PhishCollectionService(ILibraryManager libraryManager, ILogger<PhishCollectionService> logger)
        {
            _libraryManager = libraryManager;
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

                var items = _libraryManager.GetItemList(query);
                var boxSet = items.OfType<BoxSet>().FirstOrDefault();

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
        public Task<BoxSet> CreateCollectionAsync(string cityName, int year)
        {
            try
            {
                var collectionName = GetCollectionName(cityName, year);
                _logger.LogInformation("Creating new collection: {CollectionName}", collectionName);

                var boxSet = new BoxSet
                {
                    Name = collectionName,
                    Id = Guid.NewGuid(),
                    DateCreated = DateTime.UtcNow,
                    SortName = collectionName,
                    ForcedSortName = collectionName,
                    Overview = $"Multi-night Phish run in {cityName} during {year}"
                };

                // Set basic metadata
                boxSet.Genres = new[] { "Concert", "Live Music" };
                boxSet.Tags = new[] { "Phish", "Multi-Night Run", cityName, year.ToString() };

                // Get the root folder to create the collection under
                var rootFolder = _libraryManager.RootFolder;
                
                // Create the collection in Jellyfin
                _libraryManager.CreateItem(boxSet, rootFolder);

                _logger.LogInformation("Successfully created collection: {CollectionName} (ID: {CollectionId})", 
                    boxSet.Name, boxSet.Id);

                return Task.FromResult(boxSet);
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
                // Check if movie is already in the collection
                if (collection.ContainsLinkedChildByItemId(movie.Id))
                {
                    _logger.LogDebug("Movie {MovieName} already in collection {CollectionName}", 
                        movie.Name, collection.Name);
                    return false;
                }

                _logger.LogInformation("Adding movie {MovieName} to collection {CollectionName}", 
                    movie.Name, collection.Name);

                // Add the movie to the collection
                collection.AddChild(movie);

                // Update the collection in the library
                await _libraryManager.UpdateItemAsync(collection, collection.GetParent(), 
                    ItemUpdateType.MetadataEdit, cancellationToken: default);

                _logger.LogDebug("Successfully added movie {MovieName} to collection {CollectionName}", 
                    movie.Name, collection.Name);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding movie {MovieName} to collection {CollectionName}", 
                    movie.Name, collection.Name);
                return false;
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

                var collections = _libraryManager.GetItemList(query).OfType<BoxSet>();

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

                var allMovies = _libraryManager.GetItemList(query).OfType<Movie>();

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