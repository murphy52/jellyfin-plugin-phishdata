using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Models;

/// <summary>
/// Represents a Phish show from the Phish.net API.
/// </summary>
public class ShowDto
{
    /// <summary>
    /// Gets or sets the unique show identifier.
    /// </summary>
    [JsonPropertyName("showid")]
    public string ShowId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the show date in YYYY-MM-DD format.
    /// </summary>
    [JsonPropertyName("showdate")]
    public string ShowDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the venue name.
    /// </summary>
    [JsonPropertyName("venue")]
    public string Venue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the city where the show took place.
    /// </summary>
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state or province where the show took place.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the country where the show took place.
    /// </summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the venue ID for cross-referencing venue information.
    /// </summary>
    [JsonPropertyName("venueid")]
    public string? VenueId { get; set; }

    /// <summary>
    /// Gets or sets the artist name (typically "Phish").
    /// </summary>
    [JsonPropertyName("artistname")]
    public string ArtistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist ID.
    /// </summary>
    [JsonPropertyName("artistid")]
    public string? ArtistId { get; set; }

    /// <summary>
    /// Gets or sets the community rating for this show.
    /// </summary>
    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    /// <summary>
    /// Gets or sets the number of reviews for this show.
    /// </summary>
    [JsonPropertyName("reviews")]
    public string? ReviewCount { get; set; }

    /// <summary>
    /// Gets or sets additional show notes or description.
    /// </summary>
    [JsonPropertyName("shownotes")]
    public string? ShowNotes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this show has a setlist available.
    /// </summary>
    [JsonPropertyName("setlistdata")]
    public string? SetlistData { get; set; }

    /// <summary>
    /// Gets or sets the tour name or identifier.
    /// </summary>
    [JsonPropertyName("tour")]
    public string? Tour { get; set; }

    /// <summary>
    /// Gets or sets additional tags associated with the show.
    /// </summary>
    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    /// <summary>
    /// Gets the parsed show date as a DateTime object.
    /// </summary>
    [JsonIgnore]
    public DateTime? ParsedShowDate
    {
        get
        {
            if (DateTime.TryParse(ShowDate, out var date))
            {
                return date;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the full venue location as a formatted string.
    /// </summary>
    [JsonIgnore]
    public string FullLocation
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
    /// Gets the parsed community rating as a double value.
    /// </summary>
    [JsonIgnore]
    public double? ParsedRating
    {
        get
        {
            if (double.TryParse(Rating, out var rating))
            {
                return rating;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the parsed review count as an integer.
    /// </summary>
    [JsonIgnore]
    public int? ParsedReviewCount
    {
        get
        {
            if (int.TryParse(ReviewCount, out var count))
            {
                return count;
            }

            return null;
        }
    }
}