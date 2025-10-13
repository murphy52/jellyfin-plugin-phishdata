using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Providers
{
    /// <summary>
    /// Provides metadata for Phish band members appearing in concert videos.
    /// </summary>
    public class PhishPersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>
    {
        private readonly ILogger<PhishPersonProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        // Hard-coded information about Phish band members
        private static readonly Dictionary<string, PersonData> PhishMembers = new()
        {
            ["Trey Anastasio"] = new PersonData
            {
                Name = "Trey Anastasio",
                Biography = "Lead guitarist and vocalist for Phish since 1983. Known for his improvisational guitar work and songwriting.",
                BirthDate = new DateTime(1964, 9, 30),
                Roles = new[] { "Guitar", "Vocals", "Composer" }
            },
            ["Mike Gordon"] = new PersonData
            {
                Name = "Mike Gordon",
                Biography = "Bassist and vocalist for Phish since 1983. Also active as a filmmaker and solo artist.",
                BirthDate = new DateTime(1965, 6, 3),
                Roles = new[] { "Bass", "Vocals" }
            },
            ["Jon Fishman"] = new PersonData
            {
                Name = "Jon Fishman",
                Biography = "Drummer for Phish since 1983. Known for his energetic playing style and unique stage attire.",
                BirthDate = new DateTime(1965, 2, 19),
                Roles = new[] { "Drums", "Percussion" }
            },
            ["Page McConnell"] = new PersonData
            {
                Name = "Page McConnell",
                Biography = "Keyboardist and vocalist for Phish since 1985. Known for his jazz-influenced piano and Hammond organ work.",
                BirthDate = new DateTime(1963, 5, 17),
                Roles = new[] { "Keyboards", "Piano", "Vocals" }
            }
        };

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name => "Phish.net Person Provider";

        /// <summary>
        /// Initializes a new instance of the <see cref="PhishPersonProvider"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="httpClientFactory">The HTTP client factory.</param>
        public PhishPersonProvider(ILogger<PhishPersonProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Gets metadata for a Phish band member.
        /// </summary>
        /// <param name="info">The person lookup info.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The metadata result.</returns>
        public Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Person>
            {
                HasMetadata = false,
                Item = new Person()
            };

            if (string.IsNullOrEmpty(info.Name))
            {
                return Task.FromResult(result);
            }

            // Check if this is a known Phish band member
            if (PhishMembers.TryGetValue(info.Name, out var memberData))
            {
                _logger.LogDebug("Found Phish band member: {Name}", info.Name);

                var person = result.Item;
                person.Name = memberData.Name;
                person.Overview = memberData.Biography;
                
                if (memberData.BirthDate.HasValue)
                {
                    person.PremiereDate = memberData.BirthDate.Value;
                    person.ProductionYear = memberData.BirthDate.Value.Year;
                }

                // Add tags to identify this as a Phish member
                person.Tags = new[] { "Phish", "Musician", "Band Member" };

                result.HasMetadata = true;
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// Searches for people based on the provided person lookup info.
        /// </summary>
        /// <param name="searchInfo">The search information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The search results.</returns>
        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (!string.IsNullOrEmpty(searchInfo.Name) && PhishMembers.TryGetValue(searchInfo.Name, out var memberData))
            {
                var searchResult = new RemoteSearchResult
                {
                    Name = memberData.Name,
                    Overview = memberData.Biography
                };

                if (memberData.BirthDate.HasValue)
                {
                    searchResult.PremiereDate = memberData.BirthDate.Value;
                    searchResult.ProductionYear = memberData.BirthDate.Value.Year;
                }

                results.Add(searchResult);
            }

            return Task.FromResult<IEnumerable<RemoteSearchResult>>(results);
        }

        /// <summary>
        /// Gets available images for a person.
        /// </summary>
        /// <param name="item">The person item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The available remote image info.</returns>
        public Task<IEnumerable<RemoteImageInfo>> GetImageInfos(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            
            // For now, return empty list - we could add band member photos later
            // This would be where we'd return links to official photos, etc.
            
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }

        /// <summary>
        /// Gets the supported images for people.
        /// </summary>
        /// <param name="item">The person item.</param>
        /// <returns>The supported image types.</returns>
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Thumb
            };
        }

        /// <summary>
        /// Gets an image response from a URL.
        /// </summary>
        /// <param name="url">The image URL.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The HTTP response for the image.</returns>
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            return httpClient.GetAsync(url, cancellationToken);
        }

        /// <summary>
        /// Gets the HTTP client for this provider.
        /// </summary>
        /// <returns>The HTTP client.</returns>
        public HttpClient GetHttpClient()
        {
            return _httpClientFactory.CreateClient();
        }
    }

    /// <summary>
    /// Internal class to hold person data.
    /// </summary>
    internal class PersonData
    {
        public string Name { get; set; } = string.Empty;
        public string Biography { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
    }
}