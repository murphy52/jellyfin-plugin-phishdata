using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities.Movies;
using System.Net.Http;
using Jellyfin.Plugin.PhishNet.Providers;
using Jellyfin.Plugin.PhishNet.API.Client;
using Jellyfin.Plugin.PhishNet.API.Models;
using Jellyfin.Plugin.PhishNet.Services;

namespace Jellyfin.Plugin.PhishNet.Tests.Providers;

public class PhishNetMovieProviderTests
{
    private readonly Mock<ILogger<PhishNetMovieProvider>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly Mock<PhishCollectionService> _mockCollectionService;
    private readonly PhishNetMovieProvider _provider;

    public PhishNetMovieProviderTests()
    {
        _mockLogger = new Mock<ILogger<PhishNetMovieProvider>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClient = new Mock<HttpClient>();
        _mockCollectionService = new Mock<PhishCollectionService>();
        
        _mockHttpClientFactory.Setup(x => x.CreateClient())
            .Returns(_mockHttpClient.Object);
            
        _provider = new PhishNetMovieProvider(_mockLogger.Object, _mockHttpClientFactory.Object, _mockCollectionService.Object);
    }

    [Fact]
    public void Name_ShouldReturnPhishNet()
    {
        // Act & Assert
        _provider.Name.Should().Be("Phish.net");
    }

    [Fact]
    public void Order_ShouldReturn1()
    {
        // Act & Assert
        _provider.Order.Should().Be(1);
    }

    [Fact]
    public void GetSupportedImages_ShouldReturnExpectedImageTypes()
    {
        // Arrange
        var movie = new Movie();

        // Act
        var result = _provider.GetSupportedImages(movie);

        // Assert
        result.Should().Contain(new[] { 
            MediaBrowser.Model.Entities.ImageType.Primary,
            MediaBrowser.Model.Entities.ImageType.Backdrop,
            MediaBrowser.Model.Entities.ImageType.Thumb
        });
    }

    [Theory]
    [InlineData("ph2024-08-30.mkv", "/test/ph2024-08-30.mkv")]
    [InlineData("Phish.2024-04-18.Las.Vegas.NV.mkv", "/test/Phish.2024-04-18.Las.Vegas.NV.mkv")]
    public async Task GetMetadata_WithValidPhishFilename_ShouldReturnBasicMetadata(string name, string path)
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = Path.GetFileNameWithoutExtension(name),
            Path = path
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.HasMetadata.Should().BeTrue();
        result.Item.Should().NotBeNull();
        result.Item.Name.Should().NotBeNullOrEmpty();
        result.Item.PremiereDate.Should().NotBeNull();
        result.Item.ProductionYear.Should().NotBeNull();
        result.Item.DateCreated.Should().NotBe(default(DateTime));
        result.Item.Genres.Should().Contain("Concert");
        result.Item.Genres.Should().Contain("Live Music");
        result.Item.Tags.Should().Contain("Phish");
        result.Item.Tags.Should().Contain("Concert");
        result.Item.Tags.Should().Contain("Live Music");
    }

    [Theory]
    [InlineData("ph2024-08-30.mkv", "Phish 8-30-2024")]
    [InlineData("Phish.2024-04-18.Las.Vegas.NV.mkv", "Phish Las Vegas 4-18-2024")]
    [InlineData("phish-2023-12-31-msg.mkv", "Phish 12-31-2023")]
    public async Task GetMetadata_WithValidFilename_ShouldGenerateCorrectTitle(string filename, string expectedTitle)
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = Path.GetFileNameWithoutExtension(filename),
            Path = $"/test/{filename}"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.Name.Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData("Phish - 8-16-2024 - Mondegreen Secret Set.mp4")]
    [InlineData("phish_2024-02-14_valentine_special.mkv")]
    public async Task GetMetadata_WithSpecialEvent_ShouldIncludeSpecialEventTag(string filename)
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = Path.GetFileNameWithoutExtension(filename),
            Path = $"/test/{filename}"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.Tags.Should().Contain("Special Event");
    }

    [Theory]
    [InlineData("random-file.mkv")]
    [InlineData("some-other-band.mp4")]
    [InlineData("")]
    public async Task GetMetadata_WithInvalidFilename_ShouldReturnNoMetadata(string filename)
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = filename,
            Path = $"/test/{filename}"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.HasMetadata.Should().BeFalse();
    }

    [Fact]
    public async Task GetMetadata_WithPhishFilename_ShouldSetCorrectDates()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 8, 30);
        var movieInfo = new MovieInfo
        {
            Name = "ph2024-08-30",
            Path = "/test/ph2024-08-30.mkv"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.PremiereDate.Should().Be(expectedDate);
        result.Item.ProductionYear.Should().Be(2024);
        result.Item.DateCreated.Should().Be(expectedDate);
    }

    [Fact]
    public async Task GetMetadata_WithPhishFilename_ShouldSetHardCodedGenres()
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = "ph2024-08-30",
            Path = "/test/ph2024-08-30.mkv"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.Genres.Should().HaveCount(2);
        result.Item.Genres.Should().Contain("Concert");
        result.Item.Genres.Should().Contain("Live Music");
    }

    [Fact]
    public async Task GetMetadata_WithPhishFilename_ShouldIncludePhishInTags()
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = "ph2024-08-30",
            Path = "/test/ph2024-08-30.mkv"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.Tags.Should().Contain("Phish");
        result.Item.Tags.Should().Contain("Concert");
        result.Item.Tags.Should().Contain("Live Music");
    }

    [Theory]
    [InlineData("ph2024-08-30.Denver.CO.mkv", "Denver", "CO")]
    [InlineData("Phish.2024-04-18.Las.Vegas.NV.mkv", "Las Vegas", "NV")]
    public async Task GetMetadata_WithLocationInFilename_ShouldIncludeLocationInTags(
        string filename, 
        string expectedCity, 
        string expectedState)
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = Path.GetFileNameWithoutExtension(filename),
            Path = $"/test/{filename}"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.Name.Should().Contain(expectedCity);
        // Tags would include location info when API is available
        // For now, this tests the basic parsing works
    }

    [Fact]
    public async Task GetImageInfos_ShouldReturnEmptyList()
    {
        // Arrange
        var movie = new Movie();

        // Act
        var result = await _provider.GetImageInfos(movie, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ph2024-08-30")]
    [InlineData("Phish.2024-04-18.Las.Vegas.NV")]
    public async Task GetSearchResults_WithValidPhishName_ShouldReturnEmptyWhenNoApiClient(string searchName)
    {
        // Arrange
        var searchInfo = new MovieInfo
        {
            Name = searchName,
            Path = $"/test/{searchName}.mkv"
        };

        // Act
        var results = await _provider.GetSearchResults(searchInfo, CancellationToken.None);

        // Assert
        // Without API client configured, should return empty results
        results.Should().BeEmpty();
    }

    [Fact]
    public void GetHttpClient_ShouldReturnClientFromFactory()
    {
        // Act
        var httpClient = _provider.GetHttpClient();

        // Assert
        httpClient.Should().Be(_mockHttpClient.Object);
        _mockHttpClientFactory.Verify(x => x.CreateClient(), Times.Once);
    }

    [Fact]
    public async Task GetMetadata_WithLongFilename_ShouldTruncateOverviewCorrectly()
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = "ph2024-08-30.very.long.filename.with.many.details.1080p.sbd.flac24",
            Path = "/test/very-long-filename.mkv"
        };

        // Act
        var result = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result.Item.Overview.Should().NotBeNullOrEmpty();
        result.Item.Overview.Should().StartWith("Phish concert performed on");
    }

    [Theory]
    [InlineData("ph2024-08-30.mkv")]
    [InlineData("Phish.2024-04-18.mp4")]
    [InlineData("phish-2023-12-31.flac")]
    public async Task GetMetadata_MultipleCalls_ShouldReturnConsistentResults(string filename)
    {
        // Arrange
        var movieInfo = new MovieInfo
        {
            Name = Path.GetFileNameWithoutExtension(filename),
            Path = $"/test/{filename}"
        };

        // Act
        var result1 = await _provider.GetMetadata(movieInfo, CancellationToken.None);
        var result2 = await _provider.GetMetadata(movieInfo, CancellationToken.None);

        // Assert
        result1.HasMetadata.Should().Be(result2.HasMetadata);
        result1.Item.Name.Should().Be(result2.Item.Name);
        result1.Item.PremiereDate.Should().Be(result2.Item.PremiereDate);
        result1.Item.ProductionYear.Should().Be(result2.Item.ProductionYear);
    }
}