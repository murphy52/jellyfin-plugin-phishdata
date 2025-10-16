using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using Jellyfin.Plugin.PhishNet.Providers;
using Jellyfin.Plugin.PhishNet.Configuration;
using Jellyfin.Plugin.PhishNet;

namespace MetadataTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Set up logging
            using var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

            // Set up dependency injection
            var services = new ServiceCollection()
                .AddHttpClient()
                .AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug))
                .BuildServiceProvider();

            var logger = loggerFactory.CreateLogger<PhishNetMovieProvider>();
            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();

            // Initialize the plugin instance with configuration
            InitializePluginInstance();

            // Create the metadata provider
            var provider = new PhishNetMovieProvider(logger, httpClientFactory);

            // Test cases from your actual collection
            var testCases = new List<(string name, string path)>
            {
                // Original filename formats
                ("ph2024-08-06.Webcast.UntouchedTrimmed.1080p.hetyet.mkv", "/test/Mondegreen 2024/ph2024-08-06.Webcast.UntouchedTrimmed.1080p.hetyet.mkv"),
                ("Phish.2024-04-18.Las.Vegas.NV.1080p.WEB.x264.TRIMMED-WEEKaPAuG.mkv", "/test/Sphere 2024/Phish.2024-04-18.Las.Vegas.NV.1080p.WEB.x264.TRIMMED-WEEKaPAuG.mkv"),
                ("new Phish - 8-16-2024 - Mondegreen Secret Set .mp4", "/test/new Phish - 8-16-2024 - Mondegreen Secret Set .mp4"),
                ("Phish 1999.12.31 Big Cypress RAW VHS Footage.mp4", "/test/Phish 1999.12.31 Big Cypress RAW VHS Footage.mp4"),
                ("ph2023-08-31.Dicks.CO.vod.trimmed.6800BR.1080.sky.mkv", "/test/Dicks 2023/ph2023-08-31.Dicks.CO.vod.trimmed.6800BR.1080.sky.mkv"),
                // Processed title format (the problematic case)
                ("N2 Phish Hampton 11-22-1997", "/test/Concerts/Phish/Alpharetta 2025/ph1997-11-22.mkv"),
                ("ph1997-11-22.mkv", "/test/Concerts/Phish/Alpharetta 2025/ph1997-11-22.mkv")
            };

            Console.WriteLine("=== Phish.net Metadata Provider Test ===\n");

            foreach (var (name, path) in testCases)
            {
                Console.WriteLine($"🎬 Testing: {name}");
                Console.WriteLine($"📁 Path: {path}");

                try
                {
                    // Create MovieInfo (simulating what Jellyfin would provide)
                    var movieInfo = new MovieInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(name),
                        Path = path
                    };

                    // Get metadata
                    var result = await provider.GetMetadata(movieInfo, CancellationToken.None);

                    Console.WriteLine($"✅ Has Metadata: {result.HasMetadata}");

                    if (result.Item != null)
                    {
                        var movie = result.Item;
                        Console.WriteLine($"🎭 Title: {movie.Name ?? "(none)"}");
                        Console.WriteLine($"📅 Premiere Date: {movie.PremiereDate?.ToString("yyyy-MM-dd") ?? "(none)"}");
                        Console.WriteLine($"🗓️  Production Year: {movie.ProductionYear?.ToString() ?? "(none)"}");
                        Console.WriteLine($"📅 Release Date (DateCreated): {(movie.DateCreated != default(DateTime) ? movie.DateCreated.ToString("yyyy-MM-dd") : "(none)")}");
                        Console.WriteLine($"⭐ Community Rating: {(movie.CommunityRating.HasValue ? movie.CommunityRating.Value.ToString("F1") + "/10" : "(none)")}");
                        Console.WriteLine($"📝 Overview: {(movie.Overview?.Length > 200 ? movie.Overview.Substring(0, 200) + "..." : movie.Overview ?? "(none)")}");
                        
                        // Display Genres (new field)
                        if (movie.Genres?.Length > 0)
                        {
                            Console.WriteLine($"🎵 Genres: {string.Join(", ", movie.Genres)}");
                        }
                        else
                        {
                            Console.WriteLine($"🎵 Genres: (none)");
                        }
                        
                        // Display Tags
                        if (movie.Tags?.Length > 0)
                        {
                            Console.WriteLine($"🏷️  Tags: {string.Join(", ", movie.Tags)}");
                        }
                        else
                        {
                            Console.WriteLine($"🏷️  Tags: (none)");
                        }

                        // Check for provider IDs
                        var providerIds = movie.ProviderIds;
                        if (providerIds?.Count > 0)
                        {
                            Console.WriteLine($"🔗 Provider IDs:");
                            foreach (var kvp in providerIds)
                            {
                                Console.WriteLine($"   • {kvp.Key}: {kvp.Value}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"🔗 Provider IDs: (none)");
                        }
                        
                        // Display People information (note: band members need PersonProvider)
                        Console.WriteLine($"👥 People: Band members will be populated via separate PersonProvider");
                    }

                    // Test image support
                    var supportedImages = provider.GetSupportedImages(result.Item ?? new Movie());
                    Console.WriteLine($"🖼️  Supported Images: {string.Join(", ", supportedImages)}");

                    // Test image info retrieval
                    var imageInfos = await provider.GetImageInfos(result.Item ?? new Movie(), CancellationToken.None);
                    Console.WriteLine($"📸 Available Images: {imageInfos.Count()}");

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine("=== Test Summary ===");
            Console.WriteLine($"Provider Name: {provider.Name}");
            Console.WriteLine($"Provider Order: {provider.Order}");
        }

        private static void InitializePluginInstance()
        {
            // Create a mock plugin configuration
            var config = new PluginConfiguration
            {
                ApiKey = "YOUR_API_KEY_HERE" // This will be replaced when testing with real API
            };

            // For testing purposes, we'll need to mock the Plugin.Instance
            // In real usage, Jellyfin would handle this initialization
            Console.WriteLine("⚠️  Note: This test uses a mock configuration.");
            Console.WriteLine("   In a real Jellyfin environment, the plugin configuration would be loaded automatically.");
        }
    }
}