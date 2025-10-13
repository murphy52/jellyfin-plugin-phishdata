using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Models;

/// <summary>
/// Represents a Phish show setlist from the Phish.net API.
/// </summary>
public class SetlistDto
{
    /// <summary>
    /// Gets or sets the show date in YYYY-MM-DD format.
    /// </summary>
    [JsonPropertyName("showdate")]
    public string ShowDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the show ID.
    /// </summary>
    [JsonPropertyName("showid")]
    public long ShowId { get; set; }

    /// <summary>
    /// Gets or sets the venue name.
    /// </summary>
    [JsonPropertyName("venue")]
    public string Venue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the city.
    /// </summary>
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the country.
    /// </summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist name.
    /// </summary>
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the setlist data as a string.
    /// This contains the actual setlist information that needs to be parsed.
    /// </summary>
    [JsonPropertyName("setlistdata")]
    public string SetlistData { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional setlist notes.
    /// </summary>
    [JsonPropertyName("setlistnotes")]
    public string? SetlistNotes { get; set; }

    /// <summary>
    /// Gets the parsed setlist as structured data.
    /// This parses the setlistdata string into sets and songs.
    /// </summary>
    [JsonIgnore]
    public ParsedSetlist ParsedSetlist
    {
        get
        {
            if (string.IsNullOrEmpty(SetlistData))
            {
                return new ParsedSetlist();
            }

            return ParseSetlistData(SetlistData);
        }
    }

    /// <summary>
    /// Parses the raw setlist data string into structured setlist information.
    /// </summary>
    /// <param name="setlistData">The raw setlist data string.</param>
    /// <returns>A parsed setlist structure.</returns>
    private static ParsedSetlist ParseSetlistData(string setlistData)
    {
        var parsedSetlist = new ParsedSetlist();
        
        if (string.IsNullOrEmpty(setlistData))
        {
            return parsedSetlist;
        }

        // Split by common set separators
        var setParts = setlistData.Split(new[] { "Set I:", "Set II:", "Set III:", "Encore:", "Set 1:", "Set 2:", "Set 3:" }, 
            System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < setParts.Length; i++)
        {
            var setPart = setParts[i].Trim();
            if (string.IsNullOrEmpty(setPart)) continue;

            var setNumber = i + 1;
            var setName = GetSetName(i, setParts.Length);
            
            // Parse songs from the set part
            var songs = ParseSongsFromSetPart(setPart);
            
            if (songs.Count > 0)
            {
                parsedSetlist.Sets.Add(new SetInfo
                {
                    SetNumber = setNumber,
                    SetName = setName,
                    Songs = songs
                });
            }
        }

        return parsedSetlist;
    }

    private static string GetSetName(int index, int totalSets)
    {
        return index switch
        {
            0 when totalSets > 1 => "Set I",
            1 when totalSets > 2 => "Set II", 
            2 when totalSets > 3 => "Set III",
            var i when i == totalSets - 1 && totalSets > 1 => "Encore",
            _ => $"Set {index + 1}"
        };
    }

    private static List<SongInfo> ParseSongsFromSetPart(string setPart)
    {
        var songs = new List<SongInfo>();
        
        // Split by common song separators
        var songParts = setPart.Split(new[] { ",", ">" }, System.StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var songPart in songParts)
        {
            var cleanSong = songPart.Trim();
            if (string.IsNullOrEmpty(cleanSong)) continue;
            
            // Remove common annotations
            cleanSong = System.Text.RegularExpressions.Regex.Replace(cleanSong, @"\[.*?\]", "").Trim();
            cleanSong = System.Text.RegularExpressions.Regex.Replace(cleanSong, @"\(.*?\)", "").Trim();
            
            if (!string.IsNullOrEmpty(cleanSong))
            {
                songs.Add(new SongInfo
                {
                    Title = cleanSong,
                    OriginalText = songPart.Trim()
                });
            }
        }

        return songs;
    }
}

/// <summary>
/// Represents a parsed setlist with structured set and song information.
/// </summary>
public class ParsedSetlist
{
    /// <summary>
    /// Gets or sets the list of sets in the show.
    /// </summary>
    public List<SetInfo> Sets { get; set; } = new();

    /// <summary>
    /// Gets the total number of songs played.
    /// </summary>
    [JsonIgnore]
    public int TotalSongs => Sets.Sum(s => s.Songs.Count);

    /// <summary>
    /// Gets all songs from all sets as a flat list.
    /// </summary>
    [JsonIgnore]
    public List<SongInfo> AllSongs => Sets.SelectMany(s => s.Songs).ToList();
}

/// <summary>
/// Represents information about a set within a show.
/// </summary>
public class SetInfo
{
    /// <summary>
    /// Gets or sets the set number (1, 2, 3, etc.).
    /// </summary>
    public int SetNumber { get; set; }

    /// <summary>
    /// Gets or sets the set name (Set I, Set II, Encore, etc.).
    /// </summary>
    public string SetName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of songs in this set.
    /// </summary>
    public List<SongInfo> Songs { get; set; } = new();
}

/// <summary>
/// Represents information about a song within a set.
/// </summary>
public class SongInfo
{
    /// <summary>
    /// Gets or sets the cleaned song title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original text from the setlist (may include annotations).
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets any special notes about this song performance.
    /// </summary>
    public string? Notes { get; set; }
}