using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities;
using System.Net.Http;
using Jellyfin.Plugin.PhishNet.Providers;

namespace Jellyfin.Plugin.PhishNet.Tests.Providers;

public class PhishPersonProviderTests
{
    private readonly Mock<ILogger<PhishPersonProvider>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly PhishPersonProvider _provider;

    public PhishPersonProviderTests()
    {
        _mockLogger = new Mock<ILogger<PhishPersonProvider>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClient = new Mock<HttpClient>();
        
        _mockHttpClientFactory.Setup(x => x.CreateClient())
            .Returns(_mockHttpClient.Object);
            
        _provider = new PhishPersonProvider(_mockLogger.Object, _mockHttpClientFactory.Object);
    }

    [Fact]
    public void Name_ShouldReturnPhishNetPersonProvider()
    {
        // Act & Assert
        _provider.Name.Should().Be("Phish.net Person Provider");
    }

    [Fact]
    public void GetSupportedImages_ShouldReturnExpectedImageTypes()
    {
        // Arrange
        var person = new Person();

        // Act
        var result = _provider.GetSupportedImages(person);

        // Assert
        result.Should().Contain(new[] { 
            MediaBrowser.Model.Entities.ImageType.Primary,
            MediaBrowser.Model.Entities.ImageType.Thumb
        });
    }

    [Theory]
    [InlineData("Trey Anastasio")]
    [InlineData("Mike Gordon")]
    [InlineData("Jon Fishman")]
    [InlineData("Page McConnell")]
    public async Task GetMetadata_WithPhishBandMember_ShouldReturnMetadata(string bandMemberName)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = bandMemberName
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.HasMetadata.Should().BeTrue();
        result.Item.Should().NotBeNull();
        result.Item.Name.Should().Be(bandMemberName);
        result.Item.Overview.Should().NotBeNullOrEmpty();
        result.Item.PremiereDate.Should().NotBeNull();
        result.Item.ProductionYear.Should().NotBeNull();
        result.Item.Tags.Should().Contain("Phish");
        result.Item.Tags.Should().Contain("Musician");
        result.Item.Tags.Should().Contain("Band Member");
    }

    [Theory]
    [InlineData("Trey Anastasio", 1964, 9, 30)]
    [InlineData("Mike Gordon", 1965, 6, 3)]
    [InlineData("Jon Fishman", 1965, 2, 19)]
    [InlineData("Page McConnell", 1963, 5, 17)]
    public async Task GetMetadata_WithPhishBandMember_ShouldSetCorrectBirthDate(
        string bandMemberName, 
        int expectedYear, 
        int expectedMonth, 
        int expectedDay)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = bandMemberName
        };
        var expectedBirthDate = new DateTime(expectedYear, expectedMonth, expectedDay);

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.Item.PremiereDate.Should().Be(expectedBirthDate);
        result.Item.ProductionYear.Should().Be(expectedYear);
    }

    [Theory]
    [InlineData("Trey Anastasio", "Lead guitarist and vocalist for Phish since 1983")]
    [InlineData("Mike Gordon", "Bassist and vocalist for Phish since 1983")]
    [InlineData("Jon Fishman", "Drummer for Phish since 1983")]
    [InlineData("Page McConnell", "Keyboardist and vocalist for Phish since 1985")]
    public async Task GetMetadata_WithPhishBandMember_ShouldHaveCorrectBiography(
        string bandMemberName, 
        string expectedBiographyStart)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = bandMemberName
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.Item.Overview.Should().StartWith(expectedBiographyStart);
    }

    [Theory]
    [InlineData("Random Person")]
    [InlineData("Some Other Musician")]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetMetadata_WithNonPhishPerson_ShouldReturnNoMetadata(string? personName)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = personName
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.HasMetadata.Should().BeFalse();
    }

    [Theory]
    [InlineData("trey anastasio")] // lowercase
    [InlineData("TREY ANASTASIO")] // uppercase
    [InlineData("Trey  Anastasio")] // extra spaces
    public async Task GetMetadata_WithVariousNameFormats_ShouldReturnNoMetadataForNonExactMatch(string personName)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = personName
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        // Our implementation requires exact case-sensitive matches
        result.HasMetadata.Should().BeFalse();
    }

    [Theory]
    [InlineData("Trey Anastasio")]
    [InlineData("Mike Gordon")]
    [InlineData("Jon Fishman")]
    [InlineData("Page McConnell")]
    public async Task GetSearchResults_WithPhishBandMember_ShouldReturnSearchResult(string bandMemberName)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = bandMemberName
        };

        // Act
        var results = await _provider.GetSearchResults(personInfo, CancellationToken.None);

        // Assert
        results.Should().HaveCount(1);
        var result = results.First();
        result.Name.Should().Be(bandMemberName);
        result.Overview.Should().NotBeNullOrEmpty();
        result.PremiereDate.Should().NotBeNull();
        result.ProductionYear.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Random Person")]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetSearchResults_WithNonPhishPerson_ShouldReturnEmptyResults(string? personName)
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = personName
        };

        // Act
        var results = await _provider.GetSearchResults(personInfo, CancellationToken.None);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImageInfos_ShouldReturnEmptyList()
    {
        // Arrange
        var person = new Person();

        // Act
        var result = await _provider.GetImageInfos(person, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
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
    public async Task GetMetadata_MultipleCalls_ShouldReturnConsistentResults()
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = "Trey Anastasio"
        };

        // Act
        var result1 = await _provider.GetMetadata(personInfo, CancellationToken.None);
        var result2 = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result1.HasMetadata.Should().Be(result2.HasMetadata);
        result1.Item.Name.Should().Be(result2.Item.Name);
        result1.Item.Overview.Should().Be(result2.Item.Overview);
        result1.Item.PremiereDate.Should().Be(result2.Item.PremiereDate);
        result1.Item.ProductionYear.Should().Be(result2.Item.ProductionYear);
    }

    [Fact]
    public async Task GetMetadata_WithTreyAnastasio_ShouldIncludeComposerInformation()
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = "Trey Anastasio"
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.Item.Overview.Should().Contain("songwriting");
        result.Item.Overview.Should().Contain("improvisational");
    }

    [Fact]
    public async Task GetMetadata_WithMikeGordon_ShouldIncludeFilmmakerInformation()
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = "Mike Gordon"
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.Item.Overview.Should().Contain("filmmaker");
        result.Item.Overview.Should().Contain("solo artist");
    }

    [Fact]
    public async Task GetMetadata_WithJonFishman_ShouldIncludeStageAttireInformation()
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = "Jon Fishman"
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.Item.Overview.Should().Contain("energetic");
        result.Item.Overview.Should().Contain("stage attire");
    }

    [Fact]
    public async Task GetMetadata_WithPageMcConnell_ShouldIncludeJazzInformation()
    {
        // Arrange
        var personInfo = new PersonLookupInfo
        {
            Name = "Page McConnell"
        };

        // Act
        var result = await _provider.GetMetadata(personInfo, CancellationToken.None);

        // Assert
        result.Item.Overview.Should().Contain("jazz-influenced");
        result.Item.Overview.Should().Contain("Hammond organ");
    }
}