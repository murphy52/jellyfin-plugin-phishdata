using System.Net.Http;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Parsers;
using Jellyfin.Plugin.PhishNet.Providers;
using Jellyfin.Plugin.PhishNet.Services;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Providers;

/// <summary>
/// Issue #25: files that are not Phish-related must not receive the "Needs Title Fix" guidance.
/// </summary>
public class PhishNetMovieProviderNonPhishTests
{
    private static PhishNetMovieProvider CreateProvider()
    {
        var collectionService = new PhishCollectionService(
            Mock.Of<ILibraryManager>(),
            Mock.Of<ICollectionManager>(),
            Mock.Of<ILogger<PhishCollectionService>>());

        return new PhishNetMovieProvider(
            Mock.Of<ILogger<PhishNetMovieProvider>>(),
            Mock.Of<IHttpClientFactory>(),
            collectionService);
    }

    [Theory]
    [InlineData("The Matrix (1999)", "/media/Movies/The Matrix (1999)/The Matrix (1999).mkv")]
    [InlineData("Grateful Dead 1977-05-08 Barton Hall", "/media/Concerts/Dead/gd1977-05-08.mkv")]
    public async Task GetMetadata_NonPhishFile_ShouldReturnNoMetadata(string name, string path)
    {
        var result = await CreateProvider().GetMetadata(new MovieInfo { Name = name, Path = path }, CancellationToken.None);

        result.HasMetadata.Should().BeFalse();
        result.Item.Tags.Should().NotContain("Needs Title Fix");
        result.Item.Overview.Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData("Phish Live at MSG", "/media/Concerts/Phish Live at MSG.mkv")]
    [InlineData("Untitled Show", "/media/Concerts/Phish/Untitled Show.mkv")]
    public async Task GetMetadata_PhishFileWithoutParsableDate_ShouldStillProvideGuidance(string name, string path)
    {
        var result = await CreateProvider().GetMetadata(new MovieInfo { Name = name, Path = path }, CancellationToken.None);

        result.HasMetadata.Should().BeTrue();
        result.Item.Tags.Should().Contain("Needs Title Fix");
    }

    [Theory]
    [InlineData("Phish 2024-08-30", null, true)]
    [InlineData("Untitled", "/media/Phish/Untitled.mkv", true)]
    [InlineData("ph1997-11-22", "/media/Concerts/ph1997-11-22.mkv", true)]
    [InlineData("Some Show", "/media/Concerts/ph2024-08-30.mkv", true)]
    [InlineData("Phoenix Rising", "/media/Movies/Phoenix Rising.mkv", false)]
    [InlineData("The Matrix (1999)", "/media/Movies/The Matrix (1999).mkv", false)]
    [InlineData("", "", false)]
    [InlineData(null, null, false)]
    public void LooksPhishRelated_ShouldDetectPhishHints(string? name, string? path, bool expected)
    {
        PhishFileNameParser.LooksPhishRelated(name, path).Should().Be(expected);
    }
}
