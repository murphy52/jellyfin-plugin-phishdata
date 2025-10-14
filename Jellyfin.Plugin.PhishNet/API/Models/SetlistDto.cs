using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Models;

/// <summary>
/// Represents a setlist API response that may contain a permalink.
/// </summary>
public class SetlistResponse
{
    /// <summary>
    /// Gets or sets the permalink URL to the setlist on Phish.net.
    /// </summary>
    [JsonPropertyName("permalink")]
    public string? Permalink { get; set; }
    
    /// <summary>
    /// Gets or sets the setlist data (array of songs).
    /// </summary>
    [JsonPropertyName("data")]
    public List<SetlistSongDto> Data { get; set; } = new();
}

/// <summary>
/// Represents a song in a Phish setlist from the Phish.net API.
/// </summary>
public class SetlistSongDto
{
    /// <summary>
    /// Gets or sets the unique song identifier.
    /// </summary>
    [JsonPropertyName("songid")]
    public long SongId { get; set; }

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
    /// Gets or sets the song title.
    /// </summary>
    [JsonPropertyName("song")]
    public string Song { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the set number (1, 2, 3, E for encore).
    /// </summary>
    [JsonPropertyName("set")]
    public string Set { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the position of the song within the set.
    /// </summary>
    [JsonPropertyName("position")]
    public int Position { get; set; }

    /// <summary>
    /// Gets or sets the transition mark that follows this song.
    /// Common values: "," (no transition), ">" (segue), "->" (direct segue)
    /// </summary>
    [JsonPropertyName("trans_mark")]
    public string TransMark { get; set; } = ",";

    /// <summary>
    /// Gets or sets additional notes about the song performance.
    /// </summary>
    [JsonPropertyName("footnote")]
    public string? Footnote { get; set; }

    /// <summary>
    /// Gets or sets the permalink URL to the setlist page on Phish.net.
    /// </summary>
    [JsonPropertyName("permalink")]
    public string? Permalink { get; set; }

    /// <summary>
    /// Gets the clean set name for display.
    /// </summary>
    [JsonIgnore]
    public string SetName
    {
        get
        {
            return Set.ToUpperInvariant() switch
            {
                "1" or "I" => "SET 1",
                "2" or "II" => "SET 2", 
                "3" or "III" => "SET 3",
                "E" or "ENCORE" => "ENCORE",
                _ => $"SET {Set}"
            };
        }
    }

    /// <summary>
    /// Gets whether this song transitions/segues into the next song.
    /// </summary>
    [JsonIgnore]
    public bool HasTransition
    {
        get
        {
            return !string.IsNullOrEmpty(TransMark) && 
                   TransMark != "," && 
                   TransMark.Trim() != string.Empty;
        }
    }

    /// <summary>
    /// Gets the transition symbol for display (sanitized).
    /// </summary>
    [JsonIgnore]
    public string DisplayTransition
    {
        get
        {
            if (string.IsNullOrEmpty(TransMark))
                return ", ";
                
            // Common transition marks and their display equivalents
            return TransMark.Trim() switch
            {
                ">" or "->" or "→" => " > ",
                "," => ", ",
                "" => ", ",
                _ => $" {TransMark.Trim()} "
            };
        }
    }
}

/// <summary>
/// Represents a Phish show setlist from the Phish.net API.
/// This is now a collection of individual song objects with transition marks.
/// </summary>
public class SetlistDto : List<SetlistSongDto>
{
    /// <summary>
    /// Gets or sets the permalink URL to the setlist on Phish.net.
    /// </summary>
    [JsonPropertyName("permalink")]
    public string? Permalink { get; set; }
    /// <summary>
    /// Gets the parsed setlist grouped by sets with proper transition marks.
    /// </summary>
    [JsonIgnore]
    public ParsedSetlist ParsedSetlist
    {
        get
        {
            var parsedSetlist = new ParsedSetlist();
            
            if (!this.Any())
            {
                return parsedSetlist;
            }

            // Group songs by set and order properly
            var songsBySet = this
                .OrderBy(s => s.Position)
                .GroupBy(s => s.SetName)
                .OrderBy(g => GetSetOrder(g.Key));

            foreach (var setGroup in songsBySet)
            {
                var setName = setGroup.Key;
                var setSongs = setGroup.OrderBy(s => s.Position).ToList();
                
                var songs = setSongs.Select(song => new SongInfo
                {
                    Title = song.Song,
                    OriginalText = song.Song,
                    TransitionMark = song.TransMark,
                    HasTransition = song.HasTransition,
                    Notes = song.Footnote
                }).ToList();

                if (songs.Count > 0)
                {
                    parsedSetlist.Sets.Add(new SetInfo
                    {
                        SetNumber = GetSetNumber(setName),
                        SetName = setName,
                        Songs = songs
                    });
                }
            }

            return parsedSetlist;
        }
    }

    private static int GetSetOrder(string setName)
    {
        return setName switch
        {
            "SET 1" => 1,
            "SET 2" => 2,
            "SET 3" => 3,
            "ENCORE" => 99,
            _ => 50 // Unknown sets in the middle
        };
    }

    private static int GetSetNumber(string setName)
    {
        return setName switch
        {
            "SET 1" => 1,
            "SET 2" => 2,
            "SET 3" => 3,
            "ENCORE" => 4,
            _ => 1
        };
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
    /// Gets or sets the transition mark for this song.
    /// </summary>
    public string TransitionMark { get; set; } = ",";

    /// <summary>
    /// Gets or sets whether this song has a transition to the next song.
    /// </summary>
    public bool HasTransition { get; set; }

    /// <summary>
    /// Gets or sets any special notes about this song performance.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets the transition symbol for display.
    /// </summary>
    public string DisplayTransition
    {
        get
        {
            if (string.IsNullOrEmpty(TransitionMark))
                return ", ";
                
            return TransitionMark.Trim() switch
            {
                ">" or "->" or "→" => " > ",
                "," => ", ",
                "" => ", ",
                _ => $" {TransitionMark.Trim()} "
            };
        }
    }
}
