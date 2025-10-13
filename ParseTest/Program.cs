using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.PhishNet.Parsers;

namespace ParseTest
{
    class Program
    {
        static void Main(string[] args)
        {
            // Create a simple console logger
            using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            var logger = loggerFactory.CreateLogger<PhishFileNameParser>();

            var parser = new PhishFileNameParser(logger);

            // Test cases from your actual collection
            var testCases = new List<(string filename, string? directory)>
            {
                // Standard ph-format
                ("ph2024-08-06.Webcast.UntouchedTrimmed.1080p.hetyet.mkv", null),
                ("ph2025-09-12.Webcast.UntouchedTrimmed.1080p.hetyet.mkv", null),
                ("ph2023-07-15.Webcast.UntouchedTrimmed.1080p.hetyet.mkv", "Alpharetta 2023"),
                
                // Full Phish format with venue
                ("Phish.2024-04-18.Las.Vegas.NV.1080p.WEB.x264.TRIMMED-WEEKaPAuG.mkv", "Sphere 2024"),
                ("Phish.2024-04-19.Las.Vegas.NV.1080p.WEB.h264.TRIMMED-WEEKaPAuG.mkv", "Sphere 2024"),
                
                // Secret Set formats
                ("new Phish - 8-16-2024 - Mondegreen Secret Set .mp4", null),
                ("ph2024-08-16.SECRET.SET-Dover.DE.1080p.WEB.h264-WEEKaPAuG.mkv", null),
                ("Ambient Secret Set 4k.webm", null),
                
                // TV appearances
                ("Phish-2024-07-11.Evolve-The.Tonight.Show.Starring.Jimmy.Fallon.1080p.WEB.h264-WEEKaPAuG.mkv", null),
                ("Phish.2024-07-09 NPR Headquarters, Washington DC - Tiny Desk Concert  NPR - Webcast El Duderino 1080p.mkv", null),
                
                // Historical shows
                ("Phish 1999.12.31 Big Cypress RAW VHS Footage.mp4", null),
                
                // Dick's shows
                ("ph2023-08-31.Dicks.CO.vod.trimmed.6800BR.1080.sky.mkv", "Dicks 2023"),
                ("ph2023-09-01.Dicks.CO.vod.trimmed.6800BR.1080.sky.mkv", "Dicks 2023"),
                
                // Special cases that might be challenging
                ("Phish, Live at Sphere _ Exclusive Behind-the-Scenes (1080p_24fps_H264-128kbit_AAC).mp4", "Sphere 2024"),
                ("Brad Sands On Phish In The 90's & Seeing Nirvana at the Festival that inspired Clifford Ball (2160p_30fps_VP9 LQ-128kbit_AAC).mkv", null)
            };

            Console.WriteLine("=== Phish Filename Parser Test Results ===\n");

            foreach (var (filename, directory) in testCases)
            {
                Console.WriteLine($"📁 File: {filename}");
                if (!string.IsNullOrEmpty(directory))
                {
                    Console.WriteLine($"📂 Directory: {directory}");
                }

                var result = parser.Parse(filename, directory);

                Console.WriteLine($"📊 Confidence: {result.Confidence:P1}");
                
                if (result.ShowDate.HasValue)
                {
                    Console.WriteLine($"📅 Date: {result.ShowDate:yyyy-MM-dd}");
                }

                if (!string.IsNullOrEmpty(result.Venue))
                {
                    Console.WriteLine($"🏟️  Venue: {result.Venue}");
                }

                if (!string.IsNullOrEmpty(result.City) || !string.IsNullOrEmpty(result.State))
                {
                    Console.WriteLine($"📍 Location: {result.City}{(string.IsNullOrEmpty(result.State) ? "" : $", {result.State}")}");
                }

                if (!string.IsNullOrEmpty(result.ShowType))
                {
                    Console.WriteLine($"🎭 Type: {result.ShowType}");
                }

                if (result.IsSpecialEvent)
                {
                    Console.WriteLine($"⭐ Special Event: Yes");
                }

                if (result.Metadata.Count > 0)
                {
                    Console.WriteLine($"🏷️  Metadata:");
                    foreach (var kvp in result.Metadata)
                    {
                        Console.WriteLine($"   • {kvp.Key}: {kvp.Value}");
                    }
                }

                Console.WriteLine();
            }

            // Summary of parsing success
            int successfulParses = 0;
            int highConfidenceParses = 0;

            foreach (var (filename, directory) in testCases)
            {
                var result = parser.Parse(filename, directory);
                if (result.Confidence > 0.0)
                {
                    successfulParses++;
                    if (result.Confidence >= 0.7)
                    {
                        highConfidenceParses++;
                    }
                }
            }

            Console.WriteLine("=== Summary ===");
            Console.WriteLine($"Total files tested: {testCases.Count}");
            Console.WriteLine($"Successfully parsed: {successfulParses} ({successfulParses * 100.0 / testCases.Count:F1}%)");
            Console.WriteLine($"High confidence (≥70%): {highConfidenceParses} ({highConfidenceParses * 100.0 / testCases.Count:F1}%)");
        }
    }
}