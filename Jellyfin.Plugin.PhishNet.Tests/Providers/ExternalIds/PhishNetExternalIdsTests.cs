using FluentAssertions;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Xunit;
using Jellyfin.Plugin.PhishNet.Providers.ExternalIds;

namespace Jellyfin.Plugin.PhishNet.Tests.Providers.ExternalIds;

public class PhishNetExternalIdTests
{
    [Fact]
    public void PhishNetExternalId_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var externalId = new PhishNetExternalId();

        // Assert
        externalId.ProviderName.Should().Be("Phish.net");
        externalId.Key.Should().Be("PhishNet");
        externalId.Type.Should().Be(ExternalIdMediaType.Movie);
        externalId.UrlFormatString.Should().Be("https://phish.net/show/{0}");
    }

    [Fact]
    public void PhishNetExternalId_ShouldSupportMovies()
    {
        // Arrange
        var externalId = new PhishNetExternalId();
        var movie = new Movie();

        // Act
        var supports = externalId.Supports(movie);

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void PhishNetExternalId_ShouldNotSupportSeries()
    {
        // Arrange
        var externalId = new PhishNetExternalId();
        var series = new Series();

        // Act
        var supports = externalId.Supports(series);

        // Assert
        supports.Should().BeFalse();
    }
}

// Removed unused external ID provider tests since we're only keeping PhishNetExternalId

public class ExternalIdIntegrationTests
{
    [Theory]
    [InlineData("1997-11-22", "https://phish.net/show/1997-11-22")]
    [InlineData("2023-07-15", "https://phish.net/show/2023-07-15")]
    [InlineData("1995-12-31", "https://phish.net/show/1995-12-31")]
    public void PhishNetExternalId_ShouldGenerateCorrectUrls(string showDate, string expectedUrl)
    {
        // Arrange
        var externalId = new PhishNetExternalId();

        // Act
        var actualUrl = string.Format(externalId.UrlFormatString, showDate);

        // Assert
        actualUrl.Should().Be(expectedUrl);
    }

    [Fact]
    public void PhishNetExternalId_ShouldSupportMovies()
    {
        // Arrange
        var movie = new Movie();
        var externalId = new PhishNetExternalId();

        // Act & Assert
        externalId.Supports(movie).Should().BeTrue("PhishNet external ID should support movies");
    }
}
