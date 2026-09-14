using FluentAssertions;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
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
    }

    [Fact]
    public void PhishNetExternalId_ShouldSupportMovies()
    {
        // Arrange
        var externalId = new PhishNetExternalId();
        var movie = new Movie();

        // Act & Assert
        externalId.Supports(movie).Should().BeTrue();
    }

    [Fact]
    public void PhishNetExternalId_ShouldNotSupportNonMovies()
    {
        // Arrange
        var externalId = new PhishNetExternalId();
        var episode = new Episode();

        // Act & Assert
        externalId.Supports(episode).Should().BeFalse();
    }
}

public class PhishNetExternalUrlProviderTests
{
    [Fact]
    public void GetExternalUrls_ShouldReturnStoredShowUrl_ForMovie()
    {
        // Arrange
        var provider = new PhishNetExternalUrlProvider();
        var movie = new Movie();
        movie.SetProviderId(PhishNetExternalId.ProviderKey, "https://phish.net/setlists/phish-august-30-2024-dicks-sporting-goods-park-commerce-city-co-usa.html");

        // Act
        var urls = provider.GetExternalUrls(movie);

        // Assert
        provider.Name.Should().Be("Phish.net");
        urls.Should().ContainSingle().Which.Should().Be("https://phish.net/setlists/phish-august-30-2024-dicks-sporting-goods-park-commerce-city-co-usa.html");
    }

    [Fact]
    public void GetExternalUrls_ShouldReturnNothing_WhenMovieHasNoPhishNetId()
    {
        // Arrange
        var provider = new PhishNetExternalUrlProvider();
        var movie = new Movie();

        // Act & Assert
        provider.GetExternalUrls(movie).Should().BeEmpty();
    }

    [Fact]
    public void GetExternalUrls_ShouldReturnNothing_ForNonMovies()
    {
        // Arrange
        var provider = new PhishNetExternalUrlProvider();
        var episode = new Episode();
        episode.SetProviderId(PhishNetExternalId.ProviderKey, "https://phish.net/show/2024-08-30");

        // Act & Assert
        provider.GetExternalUrls(episode).Should().BeEmpty();
    }
}
