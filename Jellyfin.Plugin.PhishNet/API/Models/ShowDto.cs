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
    public long ShowId { get; set; }

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
    public int? VenueId { get; set; }

    /// <summary>
    /// Gets or sets the artist name (typically "Phish").
    /// </summary>
    [JsonPropertyName("artist_name")]
    public string ArtistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist ID.
    /// </summary>
    [JsonPropertyName("artistid")]
    public int? ArtistId { get; set; }


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
    /// Gets or sets the show year.
    /// </summary>
    [JsonPropertyName("showyear")]
    public string? ShowYear { get; set; }

    /// <summary>
    /// Gets or sets the show month.
    /// </summary>
    [JsonPropertyName("showmonth")]
    public int? ShowMonth { get; set; }

    /// <summary>
    /// Gets or sets the show day.
    /// </summary>
    [JsonPropertyName("showday")]
    public int? ShowDay { get; set; }

    /// <summary>
    /// Gets or sets the tour ID.
    /// </summary>
    [JsonPropertyName("tourid")]
    public int? TourId { get; set; }

    /// <summary>
    /// Gets or sets the tour name.
    /// </summary>
    [JsonPropertyName("tour_name")]
    public string? TourName { get; set; }

    /// <summary>
    /// Gets or sets the setlist notes with HTML formatting.
    /// </summary>
    [JsonPropertyName("setlist_notes")]
    public string? SetlistNotes { get; set; }

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

}