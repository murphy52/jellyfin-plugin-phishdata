using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.PhishNet.API.Client;
using Jellyfin.Plugin.PhishNet.API.Models;

namespace Jellyfin.Plugin.PhishNet.Tests.API;

public class PhishNetApiClientTests : IDisposable
{
    private readonly Mock<ILogger<PhishNetApiClient>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly PhishNetApiClient _apiClient;
    private const string TestApiKey = "test-api-key";

    public PhishNetApiClientTests()
    {
        _mockLogger = new Mock<ILogger<PhishNetApiClient>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://api.phish.net/")
        };
        
        _apiClient = new PhishNetApiClient(_httpClient, _mockLogger.Object, TestApiKey);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TestConnectionAsync_WithSuccessfulResponse_ShouldReturnTrue()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new ApiResponse<object>
        {
            Error = false,
            ErrorMessage = "",
            Data = new List<object> { new object() }
        });

        SetupHttpResponse(HttpStatusCode.OK, responseContent);

        // Act
        var result = await _apiClient.TestConnectionAsync(CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_WithFailedResponse_ShouldReturnFalse()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.Unauthorized, "Unauthorized");

        // Act
        var result = await _apiClient.TestConnectionAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetShowsAsync_WithValidDate_ShouldReturnShows()
    {
        // Arrange
        var expectedShows = new List<ShowDto>
        {
            new ShowDto
            {
                ShowId = 1,
                ShowDate = "2024-08-30",
                City = "Commerce City",
                State = "CO",
                VenueId = 123
            }
        };

        var responseContent = JsonSerializer.Serialize(new ApiResponse<List<ShowDto>>
        {
            Success = true,
            Data = expectedShows
        });

        SetupHttpResponse(HttpStatusCode.OK, responseContent);

        // Act
        var result = await _apiClient.GetShowsAsync("2024-08-30", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result!.First().ShowDate.Should().Be("2024-08-30");
        result.First().City.Should().Be("Commerce City");
        result.First().State.Should().Be("CO");
    }

    [Fact]
    public async Task GetShowsAsync_WithDateRange_ShouldReturnMultipleShows()
    {
        // Arrange
        var expectedShows = new List<ShowDto>
        {
            new ShowDto { ShowId = 1, ShowDate = "2024-08-30", City = "Commerce City", State = "CO" },
            new ShowDto { ShowId = 2, ShowDate = "2024-08-31", City = "Commerce City", State = "CO" },
            new ShowDto { ShowId = 3, ShowDate = "2024-09-01", City = "Commerce City", State = "CO" }
        };

        var responseContent = JsonSerializer.Serialize(new ApiResponse<List<ShowDto>>
        {
            Success = true,
            Data = expectedShows
        });

        SetupHttpResponse(HttpStatusCode.OK, responseContent);

        // Act
        var result = await _apiClient.GetShowsAsync("2024-08-30", "2024-09-01", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result!.Select(s => s.ShowDate).Should().Contain(new[] { "2024-08-30", "2024-08-31", "2024-09-01" });
    }

    [Fact]
    public async Task GetSetlistAsync_WithValidDate_ShouldReturnSetlist()
    {
        // Arrange
        var expectedSetlist = new List<SetlistDto>
        {
            new SetlistDto
            {
                ShowDate = "2024-08-30",
                ParsedSetlist = new ParsedSetlist
                {
                    TotalSongs = 23,
                    Sets = new List<SetInfo>
                    {
                        new SetInfo
                        {
                            SetName = "SET 1",
                            Songs = new List<SongInfo>
                            {
                                new SongInfo { Title = "Wilson", DisplayTransition = " > " },
                                new SongInfo { Title = "Simple", DisplayTransition = "" }
                            }
                        }
                    }
                }
            }
        };

        var responseContent = JsonSerializer.Serialize(new ApiResponse<List<SetlistDto>>
        {
            Success = true,
            Data = expectedSetlist
        });

        SetupHttpResponse(HttpStatusCode.OK, responseContent);

        // Act
        var result = await _apiClient.GetSetlistAsync("2024-08-30", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result!.First().ShowDate.Should().Be("2024-08-30");
        result.First().ParsedSetlist.Should().NotBeNull();
        result.First().ParsedSetlist!.TotalSongs.Should().Be(23);
        result.First().ParsedSetlist.Sets.Should().HaveCount(1);
        result.First().ParsedSetlist.Sets.First().Songs.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetVenueAsync_WithValidVenueId_ShouldReturnVenue()
    {
        // Arrange
        var expectedVenue = new VenueDto
        {
            VenueId = 123,
            Name = "Dick's Sporting Goods Park",
            City = "Commerce City",
            State = "CO",
            Country = "USA"
        };

        var responseContent = JsonSerializer.Serialize(new ApiResponse<VenueDto>
        {
            Success = true,
            Data = expectedVenue
        });

        SetupHttpResponse(HttpStatusCode.OK, responseContent);

        // Act
        var result = await _apiClient.GetVenueAsync("123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.VenueId.Should().Be(123);
        result.Name.Should().Be("Dick's Sporting Goods Park");
        result.City.Should().Be("Commerce City");
        result.State.Should().Be("CO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetShowsAsync_WithInvalidDate_ShouldReturnNull(string? date)
    {
        // Act
        var result = await _apiClient.GetShowsAsync(date!, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetSetlistAsync_WithInvalidDate_ShouldReturnNull(string? date)
    {
        // Act
        var result = await _apiClient.GetSetlistAsync(date!, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetVenueAsync_WithInvalidVenueId_ShouldReturnNull(string? venueId)
    {
        // Act
        var result = await _apiClient.GetVenueAsync(venueId!, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShowsAsync_WithApiError_ShouldReturnNull()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new ApiResponse<List<ShowDto>>
        {
            Success = false,
            Message = "API Error",
            Data = null
        });

        SetupHttpResponse(HttpStatusCode.OK, errorResponse);

        // Act
        var result = await _apiClient.GetShowsAsync("2024-08-30", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShowsAsync_WithHttpError_ShouldReturnNull()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Internal Server Error");

        // Act
        var result = await _apiClient.GetShowsAsync("2024-08-30", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShowsAsync_WithMalformedJson_ShouldReturnNull()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "{ invalid json }");

        // Act
        var result = await _apiClient.GetShowsAsync("2024-08-30", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShowsAsync_WithNetworkTimeout_ShouldReturnNull()
    {
        // Arrange
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        // Act
        var result = await _apiClient.GetShowsAsync("2024-08-30", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetShowsAsync_ShouldIncludeApiKeyInRequest()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ApiResponse<List<ShowDto>>
                {
                    Success = true,
                    Data = new List<ShowDto>()
                }))
            });

        // Act
        await _apiClient.GetShowsAsync("2024-08-30", CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain($"apikey={TestApiKey}");
    }

    [Fact]
    public async Task TestConnectionAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _apiClient.TestConnectionAsync(cts.Token));
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}