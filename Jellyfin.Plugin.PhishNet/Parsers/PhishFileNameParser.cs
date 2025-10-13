using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Parsers
{
    /// <summary>
    /// Represents the result of parsing a Phish show filename.
    /// </summary>
    public class PhishShowParseResult
    {
        /// <summary>
        /// Gets or sets the show date.
        /// </summary>
        public DateTime? ShowDate { get; set; }

        /// <summary>
        /// Gets or sets the venue name.
        /// </summary>
        public string? Venue { get; set; }

        /// <summary>
        /// Gets or sets the city.
        /// </summary>
        public string? City { get; set; }

        /// <summary>
        /// Gets or sets the state/province.
        /// </summary>
        public string? State { get; set; }

        /// <summary>
        /// Gets or sets the show type (e.g., "Secret Set", "Tiny Desk", "Tonight Show").
        /// </summary>
        public string? ShowType { get; set; }

        /// <summary>
        /// Gets or sets the set number for multi-set shows.
        /// </summary>
        public int? SetNumber { get; set; }

        /// <summary>
        /// Gets or sets the day number for multi-day runs.
        /// </summary>
        public int? DayNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is a special event.
        /// </summary>
        public bool IsSpecialEvent { get; set; }

        /// <summary>
        /// Gets or sets the confidence score of the parsing (0.0 to 1.0).
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Gets or sets the raw filename that was parsed.
        /// </summary>
        public string OriginalFilename { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets additional metadata found during parsing.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Parses Phish show filenames to extract show information.
    /// </summary>
    public class PhishFileNameParser
    {
        private readonly ILogger<PhishFileNameParser> _logger;

        // Common venue abbreviations and mappings
        private static readonly Dictionary<string, (string venue, string city, string state)> VenueMappings = new()
        {
            { "dicks", ("Dick's Sporting Goods Park", "Commerce City", "CO") },
            { "msg", ("Madison Square Garden", "New York", "NY") },
            { "spac", ("Saratoga Performing Arts Center", "Saratoga Springs", "NY") },
            { "sphere", ("Sphere", "Las Vegas", "NV") },
            { "alpharetta", ("Ameris Bank Amphitheatre", "Alpharetta", "GA") },
            { "charleston", ("Credit One Stadium", "Charleston", "SC") },
            { "orangebeach", ("The Wharf Amphitheater", "Orange Beach", "AL") },
            { "bigcypress", ("Big Cypress Seminole Reservation", "Big Cypress", "FL") },
            { "npr", ("NPR Headquarters", "Washington", "DC") },
            { "mondegreen", ("Mondegreen", "Dover", "DE") }
        };

        // Regex patterns for different filename formats
        private static readonly List<(Regex regex, string description, Func<Match, PhishShowParseResult> parser)> ParsePatterns = new()
        {
            // Pattern 1: ph2024-08-06.Webcast.UntouchedTrimmed.1080p.hetyet.mkv
            (new Regex(@"^ph(\d{4})-(\d{2})-(\d{2})", RegexOptions.IgnoreCase), 
             "Standard ph-format", ParseStandardPhFormat),

            // Pattern 2: Phish.2024-04-18.Las.Vegas.NV.1080p.WEB.x264.TRIMMED-WEEKaPAuG.mkv
            (new Regex(@"^Phish\.(\d{4})-(\d{2})-(\d{2})\.([^.]+)\.([A-Z]{2})", RegexOptions.IgnoreCase), 
             "Full Phish format with venue", ParseFullPhishFormat),

            // Pattern 3: new Phish - 8-16-2024 - Mondegreen Secret Set .mp4
            (new Regex(@"Phish\s*-\s*(\d{1,2})-(\d{1,2})-(\d{4})\s*-\s*(.+)", RegexOptions.IgnoreCase), 
             "Descriptive Phish format", ParseDescriptiveFormat),

            // Pattern 4: ph2024-08-16.SECRET.SET-Dover.DE.1080p.WEB.h264-WEEKaPAuG.mkv
            (new Regex(@"^ph(\d{4})-(\d{2})-(\d{2})\.SECRET\.SET-([^.]+)\.([A-Z]{2})", RegexOptions.IgnoreCase), 
             "Secret set format", ParseSecretSetFormat),

            // Pattern 5: Phish 1999.12.31 Big Cypress RAW VHS Footage.mp4
            (new Regex(@"^Phish\s+(\d{4})\.(\d{2})\.(\d{2})\s+(.+)", RegexOptions.IgnoreCase), 
             "Historical format", ParseHistoricalFormat),

            // Pattern 6: NPR Tiny Desk format
            (new Regex(@"Phish\.(\d{4})-(\d{2})-(\d{2})\s+NPR.*Tiny\s+Desk", RegexOptions.IgnoreCase), 
             "NPR Tiny Desk format", ParseNprTinyDeskFormat),

            // Pattern 7: Tonight Show format
            (new Regex(@"Phish-(\d{4})-(\d{2})-(\d{2}).*Tonight\.Show", RegexOptions.IgnoreCase), 
             "Tonight Show format", ParseTonightShowFormat),

            // Pattern 8: Special ambient/secret sets without dates
            (new Regex(@"(ambient|secret)\s+set", RegexOptions.IgnoreCase), 
             "Ambient/Secret set without date", ParseAmbientSpecialFormat),

            // Pattern 9: Generic date patterns as fallback
            (new Regex(@"(\d{4})-(\d{2})-(\d{2})", RegexOptions.IgnoreCase), 
             "Generic date fallback", ParseGenericDateFormat)
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishFileNameParser"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public PhishFileNameParser(ILogger<PhishFileNameParser> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parses a filename to extract Phish show information.
        /// </summary>
        /// <param name="filename">The filename to parse.</param>
        /// <param name="directoryPath">Optional directory path for additional context.</param>
        /// <returns>The parse result.</returns>
        public PhishShowParseResult Parse(string filename, string? directoryPath = null)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return new PhishShowParseResult { Confidence = 0.0 };
            }

            var cleanFilename = Path.GetFileNameWithoutExtension(filename);
            _logger.LogDebug("Parsing filename: {Filename}", cleanFilename);

            var results = new List<PhishShowParseResult>();

            // Try each parsing pattern
            foreach (var (regex, description, parser) in ParsePatterns)
            {
                var match = regex.Match(cleanFilename);
                if (match.Success)
                {
                    try
                    {
                        var result = parser(match);
                        if (result != null)
                        {
                            result.OriginalFilename = filename;
                            
                            // Apply directory context if available
                            ApplyDirectoryContext(result, directoryPath);
                            
                            results.Add(result);
                            _logger.LogDebug("Pattern '{Description}' matched with confidence {Confidence}", 
                                description, result.Confidence);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing filename with pattern '{Description}'", description);
                    }
                }
            }

            // Return the result with the highest confidence
            var bestResult = results.OrderByDescending(r => r.Confidence).FirstOrDefault();
            
            if (bestResult == null)
            {
                _logger.LogInformation("No patterns matched for filename: {Filename}", cleanFilename);
                return new PhishShowParseResult 
                { 
                    OriginalFilename = filename,
                    Confidence = 0.0 
                };
            }

            _logger.LogDebug("Best parse result for '{Filename}': {Date} at {Venue} (confidence: {Confidence})", 
                cleanFilename, bestResult.ShowDate?.ToString("yyyy-MM-dd"), bestResult.Venue, bestResult.Confidence);

            return bestResult;
        }

        /// <summary>
        /// Applies additional context from the directory path.
        /// </summary>
        /// <param name="result">The parse result to enhance.</param>
        /// <param name="directoryPath">The directory path.</param>
        private static void ApplyDirectoryContext(PhishShowParseResult result, string? directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                return;
            }

            var directoryName = Path.GetFileName(directoryPath)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(directoryName))
            {
                return;
            }

            // Extract venue from directory name if not already set
            if (string.IsNullOrEmpty(result.Venue))
            {
                foreach (var (key, (venue, city, state)) in VenueMappings)
                {
                    if (directoryName.Contains(key))
                    {
                        result.Venue ??= venue;
                        result.City ??= city;
                        result.State ??= state;
                        result.Confidence += 0.1; // Boost confidence for directory context
                        break;
                    }
                }
            }

            // Handle year folders
            if (Regex.IsMatch(directoryName, @"\d{4}"))
            {
                result.Confidence += 0.05; // Small boost for year context
            }
        }

        // Parser methods for each pattern type...
        private static PhishShowParseResult ParseStandardPhFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);

            return new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                Confidence = 0.8 // High confidence for standard format
            };
        }

        private static PhishShowParseResult ParseFullPhishFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);
            var city = match.Groups[4].Value.Replace(".", " ");
            var state = match.Groups[5].Value;

            var result = new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                City = city,
                State = state,
                Confidence = 0.9 // Very high confidence for full format
            };

            // Special case for Las Vegas - map to Sphere if appropriate
            if (city.Equals("Las Vegas", StringComparison.OrdinalIgnoreCase) && state.Equals("NV", StringComparison.OrdinalIgnoreCase))
            {
                // Could be Sphere, but we'll let directory context handle this
                result.City = "Las Vegas";
            }

            return result;
        }

        private static PhishShowParseResult ParseDescriptiveFormat(Match match)
        {
            var month = int.Parse(match.Groups[1].Value);
            var day = int.Parse(match.Groups[2].Value);
            var year = int.Parse(match.Groups[3].Value);
            var description = match.Groups[4].Value.Trim();

            var result = new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                Confidence = 0.7
            };

            // Check for special event types
            if (description.ToLowerInvariant().Contains("secret set"))
            {
                result.ShowType = "Secret Set";
                result.IsSpecialEvent = true;
                result.Confidence += 0.1;
            }

            // Extract venue from description
            var descLower = description.ToLowerInvariant();
            foreach (var (key, (venue, city, state)) in VenueMappings)
            {
                if (descLower.Contains(key))
                {
                    result.Venue = venue;
                    result.City = city;
                    result.State = state;
                    result.Confidence += 0.1;
                    break;
                }
            }

            return result;
        }

        private static PhishShowParseResult ParseSecretSetFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);
            var city = match.Groups[4].Value.Replace(".", " ");
            var state = match.Groups[5].Value;

            return new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                City = city,
                State = state,
                ShowType = "Secret Set",
                IsSpecialEvent = true,
                Confidence = 0.9
            };
        }

        private static PhishShowParseResult ParseHistoricalFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);
            var description = match.Groups[4].Value.Trim();

            var result = new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                Confidence = 0.8
            };

            // Handle Big Cypress specifically
            if (description.ToLowerInvariant().Contains("big cypress"))
            {
                result.Venue = "Big Cypress Seminole Reservation";
                result.City = "Big Cypress";
                result.State = "FL";
                result.IsSpecialEvent = true;
                result.ShowType = "Millennium Show";
                result.Confidence = 0.95;
            }

            return result;
        }

        private static PhishShowParseResult ParseNprTinyDeskFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);

            return new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                Venue = "NPR Headquarters",
                City = "Washington",
                State = "DC",
                ShowType = "NPR Tiny Desk Concert",
                IsSpecialEvent = true,
                Confidence = 0.95
            };
        }

        private static PhishShowParseResult ParseTonightShowFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);

            return new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                Venue = "Studio 6B",
                City = "New York",
                State = "NY",
                ShowType = "The Tonight Show Starring Jimmy Fallon",
                IsSpecialEvent = true,
                Confidence = 0.9
            };
        }

        private static PhishShowParseResult ParseAmbientSpecialFormat(Match match)
        {
            var setType = match.Groups[1].Value;

            return new PhishShowParseResult
            {
                ShowType = $"{char.ToUpper(setType[0])}{setType.Substring(1)} Set",
                IsSpecialEvent = true,
                Confidence = 0.6 // Moderate confidence for special sets without dates
            };
        }

        private static PhishShowParseResult ParseGenericDateFormat(Match match)
        {
            var year = int.Parse(match.Groups[1].Value);
            var month = int.Parse(match.Groups[2].Value);
            var day = int.Parse(match.Groups[3].Value);

            return new PhishShowParseResult
            {
                ShowDate = new DateTime(year, month, day),
                Confidence = 0.3 // Low confidence for generic fallback
            };
        }
    }
}