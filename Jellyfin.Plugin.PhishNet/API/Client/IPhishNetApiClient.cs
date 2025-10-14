using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.API.Models;

namespace Jellyfin.Plugin.PhishNet.API.Client;

/// <summary>
/// Interface for Phish.net API client operations.
/// </summary>
public interface IPhishNetApiClient
{
    /// <summary>
    /// Gets shows for a specific date.
    /// </summary>
    /// <param name="showDate">The show date in YYYY-MM-DD format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of shows for the specified date.</returns>
    Task<List<ShowDto>> GetShowsAsync(string showDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets shows for a specific date range.
    /// </summary>
    /// <param name="startDate">The start date in YYYY-MM-DD format.</param>
    /// <param name="endDate">The end date in YYYY-MM-DD format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of shows in the specified date range.</returns>
    Task<List<ShowDto>> GetShowsAsync(string startDate, string endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets shows for a specific year.
    /// </summary>
    /// <param name="year">The year to get shows for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of shows for the specified year.</returns>
    Task<List<ShowDto>> GetShowsByYearAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets setlist information for a specific show date.
    /// </summary>
    /// <param name="showDate">The show date in YYYY-MM-DD format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The setlist for the specified show.</returns>
    Task<List<SetlistDto>> GetSetlistAsync(string showDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets venue information by venue ID.
    /// </summary>
    /// <param name="venueId">The venue ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Venue information.</returns>
    Task<VenueDto?> GetVenueAsync(string venueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets venue information by venue ID.
    /// </summary>
    /// <param name="venueId">The venue ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Venue information.</returns>
    Task<VenueDto?> GetVenueAsync(int venueId, CancellationToken cancellationToken = default);


    /// <summary>
    /// Tests the API connection and validates the API key.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the connection is successful and API key is valid.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}