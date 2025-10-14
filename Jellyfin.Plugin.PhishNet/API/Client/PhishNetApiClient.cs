using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.PhishNet.API.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.API.Client;

/// <summary>
/// Implementation of the Phish.net API client.
/// </summary>
public class PhishNetApiClient : IPhishNetApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PhishNetApiClient> _logger;
    private readonly string _apiKey;
    private readonly SemaphoreSlim _rateLimitSemaphore;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(1); // 1 request per second

    /// <summary>
    /// Initializes a new instance of the <see cref="PhishNetApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="apiKey">The Phish.net API key.</param>
    public PhishNetApiClient(HttpClient httpClient, ILogger<PhishNetApiClient> logger, string apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiKey = string.IsNullOrEmpty(apiKey) ? throw new ArgumentException("API key cannot be null or empty", nameof(apiKey)) : apiKey;
        
        _rateLimitSemaphore = new SemaphoreSlim(1, 1);
        
        // Configure HTTP client
        _httpClient.BaseAddress ??= new Uri(Constants.PhishNetApiBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(Constants.HttpClientTimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<List<ShowDto>> GetShowsAsync(string showDate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(showDate))
        {
            throw new ArgumentException("Show date cannot be null or empty", nameof(showDate));
        }

        var endpoint = $"shows/showdate/{showDate}.json";
        var response = await MakeApiRequestAsync<ShowDto>(endpoint, cancellationToken).ConfigureAwait(false);
        
        return response?.Data ?? new List<ShowDto>();
    }

    /// <inheritdoc />
    public async Task<List<ShowDto>> GetShowsAsync(string startDate, string endDate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(startDate))
        {
            throw new ArgumentException("Start date cannot be null or empty", nameof(startDate));
        }
        if (string.IsNullOrEmpty(endDate))
        {
            throw new ArgumentException("End date cannot be null or empty", nameof(endDate));
        }

        var endpoint = $"shows/artist/{Constants.PhishArtistName}.json";
        var queryParams = new Dictionary<string, string>
        {
            ["startdate"] = startDate,
            ["enddate"] = endDate
        };
        
        var response = await MakeApiRequestAsync<ShowDto>(endpoint, cancellationToken, queryParams).ConfigureAwait(false);
        return response?.Data ?? new List<ShowDto>();
    }

    /// <inheritdoc />
    public async Task<List<ShowDto>> GetShowsByYearAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year < 1983 || year > DateTime.Now.Year + 1)
        {
            throw new ArgumentException($"Year must be between 1983 and {DateTime.Now.Year + 1}", nameof(year));
        }

        var endpoint = $"shows/showyear/{year}.json";
        var response = await MakeApiRequestAsync<ShowDto>(endpoint, cancellationToken).ConfigureAwait(false);
        
        return response?.Data ?? new List<ShowDto>();
    }

    /// <inheritdoc />
    public async Task<List<SetlistDto>> GetSetlistAsync(string showDate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(showDate))
        {
            throw new ArgumentException("Show date cannot be null or empty", nameof(showDate));
        }

        var endpoint = $"setlists/showdate/{showDate}.json";
        var response = await MakeApiRequestAsync<SetlistSongDto>(endpoint, cancellationToken).ConfigureAwait(false);
        
        // Convert the individual song objects to a SetlistDto
        var songs = response?.Data ?? new List<SetlistSongDto>();
        var setlistDto = new SetlistDto();
        setlistDto.AddRange(songs);
        
        return new List<SetlistDto> { setlistDto };
    }

    /// <inheritdoc />
    public async Task<VenueDto?> GetVenueAsync(string venueId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(venueId))
        {
            throw new ArgumentException("Venue ID cannot be null or empty", nameof(venueId));
        }

        var endpoint = $"venues/venueid/{venueId}.json";
        var response = await MakeApiRequestAsync<VenueDto>(endpoint, cancellationToken).ConfigureAwait(false);
        
        return response?.Data?.FirstOrDefault();
    }

    /// <summary>
    /// Gets venue information by venue ID.
    /// </summary>
    /// <param name="venueId">The venue ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Venue information.</returns>
    public async Task<VenueDto?> GetVenueAsync(int venueId, CancellationToken cancellationToken = default)
    {
        var endpoint = $"venues/venueid/{venueId}.json";
        var response = await MakeApiRequestAsync<VenueDto>(endpoint, cancellationToken).ConfigureAwait(false);
        
        return response?.Data?.FirstOrDefault();
    }


    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Test with a simple API call to get recent shows
            var endpoint = "shows.json";
            var queryParams = new Dictionary<string, string>
            {
                ["limit"] = "1"
            };
            
            var response = await MakeApiRequestAsync<ShowDto>(endpoint, cancellationToken, queryParams).ConfigureAwait(false);
            return response != null && response.IsSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test API connection");
            return false;
        }
    }

    /// <summary>
    /// Makes a request to the Phish.net API with rate limiting and error handling.
    /// </summary>
    /// <typeparam name="T">The type of data expected in the response.</typeparam>
    /// <param name="endpoint">The API endpoint to call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="additionalParams">Additional query parameters.</param>
    /// <returns>The API response.</returns>
    private async Task<ApiResponse<T>?> MakeApiRequestAsync<T>(
        string endpoint, 
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? additionalParams = null)
    {
        await ApplyRateLimitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var url = BuildApiUrl(endpoint, additionalParams);
            
            _logger.LogDebug("Making API request to: {Url}", url.Replace(_apiKey, "***"));
            
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("API request failed with status code: {StatusCode}", response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            
            if (string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogWarning("API returned empty response");
                return null;
            }

            var apiResponse = JsonSerializer.Deserialize<ApiResponse<T>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse != null && !apiResponse.IsSuccess)
            {
                _logger.LogWarning("API returned error: {Error} - {ErrorMessage}", apiResponse.Error, apiResponse.ErrorMessage);
            }

            return apiResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during API request to {Endpoint}", endpoint);
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timeout for API endpoint {Endpoint}", endpoint);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response from API endpoint {Endpoint}", endpoint);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during API request to {Endpoint}", endpoint);
            throw;
        }
    }

    /// <summary>
    /// Builds the complete API URL with query parameters.
    /// </summary>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="additionalParams">Additional query parameters.</param>
    /// <returns>The complete URL.</returns>
    private string BuildApiUrl(string endpoint, Dictionary<string, string>? additionalParams = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["apikey"] = _apiKey
        };

        if (additionalParams != null)
        {
            foreach (var param in additionalParams)
            {
                queryParams[param.Key] = param.Value;
            }
        }

        var query = string.Join("&", queryParams.Select(kvp => 
            $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        return $"{endpoint}?{query}";
    }

    /// <summary>
    /// Applies rate limiting to prevent API abuse.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task ApplyRateLimitAsync(CancellationToken cancellationToken)
    {
        await _rateLimitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            
            if (timeSinceLastRequest < _rateLimitDelay)
            {
                var delayTime = _rateLimitDelay - timeSinceLastRequest;
                _logger.LogDebug("Rate limiting: waiting {DelayMs}ms", delayTime.TotalMilliseconds);
                await Task.Delay(delayTime, cancellationToken).ConfigureAwait(false);
            }
            
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    /// <summary>
    /// Disposes the HTTP client and other resources.
    /// </summary>
    public void Dispose()
    {
        _rateLimitSemaphore?.Dispose();
        _httpClient?.Dispose();
    }
}