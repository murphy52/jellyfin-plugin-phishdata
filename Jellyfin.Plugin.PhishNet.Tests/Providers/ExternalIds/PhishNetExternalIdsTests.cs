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

public class PhishNetSetlistExternalIdTests
{
    [Fact]
    public void PhishNetSetlistExternalId_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var externalId = new PhishNetSetlistExternalId();

        // Assert
        externalId.ProviderName.Should().Be("Phish.net Setlist");
        externalId.Key.Should().Be("PhishNetSetlist");
        externalId.Type.Should().Be(ExternalIdMediaType.Movie);
        externalId.UrlFormatString.Should().Be("https://phish.net/setlists/{0}");
    }

    [Fact]
    public void PhishNetSetlistExternalId_ShouldSupportMovies()
    {
        // Arrange
        var externalId = new PhishNetSetlistExternalId();
        var movie = new Movie();

        // Act
        var supports = externalId.Supports(movie);

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void PhishNetSetlistExternalId_ShouldNotSupportSeries()
    {
        // Arrange
        var externalId = new PhishNetSetlistExternalId();
        var series = new Series();

        // Act
        var supports = externalId.Supports(series);

        // Assert
        supports.Should().BeFalse();
    }
}

public class PhishNetVenueExternalIdTests
{
    [Fact]
    public void PhishNetVenueExternalId_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var externalId = new PhishNetVenueExternalId();

        // Assert
        externalId.ProviderName.Should().Be("Phish.net Venue");
        externalId.Key.Should().Be("PhishNetVenue");
        externalId.Type.Should().Be(ExternalIdMediaType.Movie);
        externalId.UrlFormatString.Should().Be("https://phish.net/venue/{0}");
    }

    [Fact]
    public void PhishNetVenueExternalId_ShouldSupportMovies()
    {
        // Arrange
        var externalId = new PhishNetVenueExternalId();
        var movie = new Movie();

        // Act
        var supports = externalId.Supports(movie);

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void PhishNetVenueExternalId_ShouldNotSupportSeries()
    {
        // Arrange
        var externalId = new PhishNetVenueExternalId();
        var series = new Series();

        // Act
        var supports = externalId.Supports(series);

        // Assert
        supports.Should().BeFalse();
    }
}

public class PhishNetReviewsExternalIdTests
{
    [Fact]
    public void PhishNetReviewsExternalId_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var externalId = new PhishNetReviewsExternalId();

        // Assert
        externalId.ProviderName.Should().Be("Phish.net Reviews");
        externalId.Key.Should().Be("PhishNetReviews");
        externalId.Type.Should().Be(ExternalIdMediaType.Movie);
        externalId.UrlFormatString.Should().Be("https://phish.net/show/{0}/reviews");
    }

    [Fact]
    public void PhishNetReviewsExternalId_ShouldSupportMovies()
    {
        // Arrange
        var externalId = new PhishNetReviewsExternalId();
        var movie = new Movie();

        // Act
        var supports = externalId.Supports(movie);

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void PhishNetReviewsExternalId_ShouldNotSupportSeries()
    {
        // Arrange
        var externalId = new PhishNetReviewsExternalId();
        var series = new Series();

        // Act
        var supports = externalId.Supports(series);

        // Assert
        supports.Should().BeFalse();
    }

    [Fact]
    public void PhishNetReviewsExternalId_ShouldGenerateCorrectUrl()
    {
        // Arrange
        var externalId = new PhishNetReviewsExternalId();
        var showDate = "1997-11-22";

        // Act
        var expectedUrl = string.Format(externalId.UrlFormatString, showDate);

        // Assert
        expectedUrl.Should().Be("https://phish.net/show/1997-11-22/reviews");
    }
}

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

    [Theory]
    [InlineData("1997-11-22", "https://phish.net/setlists/1997-11-22")]
    [InlineData("2023-07-15", "https://phish.net/setlists/2023-07-15")]
    [InlineData("1995-12-31", "https://phish.net/setlists/1995-12-31")]
    public void PhishNetSetlistExternalId_ShouldGenerateCorrectUrls(string showDate, string expectedUrl)
    {
        // Arrange
        var externalId = new PhishNetSetlistExternalId();

        // Act
        var actualUrl = string.Format(externalId.UrlFormatString, showDate);

        // Assert
        actualUrl.Should().Be(expectedUrl);
    }

    [Theory]
    [InlineData("123", "https://phish.net/venue/123")]
    [InlineData("456", "https://phish.net/venue/456")]
    [InlineData("789", "https://phish.net/venue/789")]
    public void PhishNetVenueExternalId_ShouldGenerateCorrectUrls(string venueId, string expectedUrl)
    {
        // Arrange
        var externalId = new PhishNetVenueExternalId();

        // Act
        var actualUrl = string.Format(externalId.UrlFormatString, venueId);

        // Assert
        actualUrl.Should().Be(expectedUrl);
    }

    [Fact]
    public void AllExternalIds_ShouldHaveUniqueKeys()
    {
        // Arrange
        var externalIds = new[]
        {
            new PhishNetExternalId(),
            new PhishNetSetlistExternalId(),
            new PhishNetVenueExternalId(),
            new PhishNetReviewsExternalId()
        };

        // Act
        var keys = externalIds.Select(e => e.Key).ToList();

        // Assert
        keys.Should().OnlyHaveUniqueItems("Each external ID should have a unique key");
        keys.Should().HaveCount(4, "All external IDs should be included");
    }

    [Fact]
    public void AllExternalIds_ShouldSupportMovies()
    {
        // Arrange
        var movie = new Movie();
        var externalIds = new[]
        {
            new PhishNetExternalId(),
            new PhishNetSetlistExternalId(),
            new PhishNetVenueExternalId(),
            new PhishNetReviewsExternalId()
        };

        // Act & Assert
        foreach (var externalId in externalIds)
        {
            externalId.Supports(movie).Should().BeTrue($"{externalId.ProviderName} should support movies");
        }
    }
}