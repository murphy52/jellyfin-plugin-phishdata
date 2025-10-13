using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Models;

/// <summary>
/// Represents a venue from the Phish.net API.
/// </summary>
public class VenueDto
{
    /// <summary>
    /// Gets or sets the venue ID.
    /// </summary>
    [JsonPropertyName("venueid")]
    public int VenueId { get; set; }

    /// <summary>
    /// Gets or sets the venue name.
    /// </summary>
    [JsonPropertyName("venue")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the city where the venue is located.
    /// </summary>
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state where the venue is located.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the country where the venue is located.
    /// </summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional venue information or notes.
    /// </summary>
    [JsonPropertyName("venueinfo")]
    public string? VenueInfo { get; set; }

    /// <summary>
    /// Gets or sets the venue capacity if available.
    /// </summary>
    [JsonPropertyName("capacity")]
    public string? Capacity { get; set; }

    /// <summary>
    /// Gets the full address of the venue.
    /// </summary>
    [JsonIgnore]
    public string FullAddress
    {
        get
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrEmpty(City))
            {
                parts.Add(City);
            }

            if (!string.IsNullOrEmpty(State))
            {
                parts.Add(State);
            }

            if (!string.IsNullOrEmpty(Country) && Country != "USA")
            {
                parts.Add(Country);
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Gets the parsed capacity as an integer value.
    /// </summary>
    [JsonIgnore]
    public int? ParsedCapacity
    {
        get
        {
            if (int.TryParse(Capacity, out var capacity))
            {
                return capacity;
            }

            return null;
        }
    }
}