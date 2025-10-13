using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Models;

/// <summary>
/// Generic response wrapper for all Phish.net API calls.
/// </summary>
/// <typeparam name="T">The type of data returned in the response.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Gets or sets the error code. 0 indicates success.
    /// </summary>
    [JsonPropertyName("error")]
    public int Error { get; set; }

    /// <summary>
    /// Gets or sets the error message. Empty if no error occurred.
    /// </summary>
    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the response data.
    /// </summary>
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether the API call was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Error == 0;

    /// <summary>
    /// Gets a value indicating whether the response contains any data.
    /// </summary>
    [JsonIgnore]
    public bool HasData => Data != null && Data.Count > 0;
}

/// <summary>
/// Non-generic API response for single-item responses.
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    /// <summary>
    /// Gets or sets the single item response data.
    /// </summary>
    [JsonPropertyName("data")]
    public new object? Data { get; set; }
}