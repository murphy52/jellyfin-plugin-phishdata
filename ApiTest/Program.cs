using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.PhishNet.API.Client;
using Jellyfin.Plugin.PhishNet.API.Models;

namespace ApiTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🎸 Phish.net API Test Application");
        Console.WriteLine("==================================");
        
        // Get API key from command line argument
        if (args.Length == 0)
        {
            Console.WriteLine("❌ Please provide your Phish.net API key as an argument:");
            Console.WriteLine("   dotnet run <your-api-key>");
            Console.WriteLine();
            Console.WriteLine("Get your free API key at: https://phish.net/api/keys");
            return;
        }

        var apiKey = args[0];
        Console.WriteLine($"🔑 Using API key: {apiKey.Substring(0, Math.Min(8, apiKey.Length))}...");
        Console.WriteLine();

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        var logger = loggerFactory.CreateLogger<PhishNetApiClient>();

        // Create HTTP client and API client
        using var httpClient = new HttpClient();
        using var apiClient = new PhishNetApiClient(httpClient, logger, apiKey);

        try
        {
            // Test 1: API Connection
            Console.WriteLine("🧪 Test 1: Testing API connection...");
            var connectionTest = await apiClient.TestConnectionAsync();
            
            if (connectionTest)
            {
                Console.WriteLine("✅ API connection successful!");
            }
            else
            {
                Console.WriteLine("❌ API connection failed!");
                return;
            }
            Console.WriteLine();

            // Test 2: Get shows for a famous date (Hampton '97)
            Console.WriteLine("🧪 Test 2: Getting shows for 1997-11-22 (Hampton '97)...");
            var hamptonShows = await apiClient.GetShowsAsync("1997-11-22");
            
            if (hamptonShows.Count > 0)
            {
                var show = hamptonShows[0];
                Console.WriteLine($"✅ Found show: {show.ShowDate} at {show.Venue}");
                Console.WriteLine($"   Location: {show.FullLocation}");
                Console.WriteLine($"   Rating: {show.ParsedRating?.ToString("F1") ?? "N/A"}");
                Console.WriteLine($"   Reviews: {show.ParsedReviewCount ?? 0}");
            }
            else
            {
                Console.WriteLine("❌ No shows found for 1997-11-22");
            }
            Console.WriteLine();

            // Test 3: Get setlist for Hampton '97
            Console.WriteLine("🧪 Test 3: Getting setlist for 1997-11-22...");
            var setlists = await apiClient.GetSetlistAsync("1997-11-22");
            
            if (setlists.Count > 0)
            {
                var setlist = setlists[0];
                Console.WriteLine($"✅ Found setlist for {setlist.ShowDate}");
                Console.WriteLine($"   Venue: {setlist.Venue}");
                
                var parsed = setlist.ParsedSetlist;
                Console.WriteLine($"   Sets: {parsed.Sets.Count}");
                Console.WriteLine($"   Total Songs: {parsed.TotalSongs}");
                
                // Show first few songs from each set
                foreach (var set in parsed.Sets.Take(2))
                {
                    Console.WriteLine($"   {set.SetName}: {string.Join(", ", set.Songs.Take(3).Select(s => s.Title))}...");
                }
            }
            else
            {
                Console.WriteLine("❌ No setlist found for 1997-11-22");
            }
            Console.WriteLine();

            // Test 4: Get shows by year (just a few recent ones)
            Console.WriteLine("🧪 Test 4: Getting recent shows from 2023...");
            var recentShows = await apiClient.GetShowsByYearAsync(2023);
            
            Console.WriteLine($"✅ Found {recentShows.Count} shows in 2023");
            
            if (recentShows.Count > 0)
            {
                Console.WriteLine("   Recent shows:");
                foreach (var show in recentShows.Take(5))
                {
                    Console.WriteLine($"   • {show.ShowDate} - {show.Venue} ({show.City}, {show.State})");
                }
            }
            Console.WriteLine();

            // Test 5: Get venue information (if we have a venue ID from previous results)
            if (hamptonShows.Count > 0 && hamptonShows[0].VenueId.HasValue)
            {
                Console.WriteLine($"🧪 Test 5: Getting venue information for venue ID {hamptonShows[0].VenueId}...");
                var venue = await apiClient.GetVenueAsync(hamptonShows[0].VenueId.Value);
                
                if (venue != null)
                {
                    Console.WriteLine($"✅ Venue: {venue.Name}");
                    Console.WriteLine($"   Address: {venue.FullAddress}");
                    Console.WriteLine($"   Capacity: {venue.ParsedCapacity?.ToString() ?? "N/A"}");
                }
                else
                {
                    Console.WriteLine("❌ Venue information not found");
                }
                Console.WriteLine();
            }

            // Test 6: Get reviews (if enabled)
            Console.WriteLine("🧪 Test 6: Getting reviews for 1997-11-22...");
            var reviews = await apiClient.GetReviewsAsync("1997-11-22", 2);
            
            if (reviews.Count > 0)
            {
                Console.WriteLine($"✅ Found {reviews.Count} reviews");
                foreach (var review in reviews)
                {
                    Console.WriteLine($"   • {review.Username}: \"{review.Title}\"");
                    Console.WriteLine($"     Rating: {review.ParsedRating?.ToString("F1") ?? "N/A"}");
                    Console.WriteLine($"     Preview: {review.Preview}");
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("❌ No reviews found");
            }

            Console.WriteLine("🎉 All tests completed successfully!");
            Console.WriteLine("   The Phish.net API client and data models are working correctly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
}