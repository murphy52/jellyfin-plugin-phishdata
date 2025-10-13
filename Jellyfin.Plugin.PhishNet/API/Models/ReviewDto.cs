using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Models;

/// <summary>
/// Represents a show review from the Phish.net API.
/// </summary>
public class ReviewDto
{
    /// <summary>
    /// Gets or sets the review ID.
    /// </summary>
    [JsonPropertyName("reviewid")]
    public int ReviewId { get; set; }

    /// <summary>
    /// Gets or sets the show date this review is for.
    /// </summary>
    [JsonPropertyName("showdate")]
    public string ShowDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the show ID this review is for.
    /// </summary>
    [JsonPropertyName("showid")]
    public int ShowId { get; set; }

    /// <summary>
    /// Gets or sets the username of the reviewer.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the review title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the review content/body.
    /// </summary>
    [JsonPropertyName("review")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the rating given by the reviewer.
    /// </summary>
    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    /// <summary>
    /// Gets or sets the date the review was posted.
    /// </summary>
    [JsonPropertyName("posted_date")]
    public string? PostedDate { get; set; }

    /// <summary>
    /// Gets or sets any additional review tags or metadata.
    /// </summary>
    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    /// <summary>
    /// Gets the parsed rating as a double value.
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
    /// Gets the parsed posted date as a DateTime object.
    /// </summary>
    [JsonIgnore]
    public DateTime? ParsedPostedDate
    {
        get
        {
            if (DateTime.TryParse(PostedDate, out var date))
            {
                return date;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets a shortened version of the review content for previews.
    /// </summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
            {
                return string.Empty;
            }

            const int maxLength = 200;
            if (Content.Length <= maxLength)
            {
                return Content;
            }

            return Content.Substring(0, maxLength) + "...";
        }
    }
}