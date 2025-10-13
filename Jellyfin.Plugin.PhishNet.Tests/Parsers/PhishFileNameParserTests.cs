using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Jellyfin.Plugin.PhishNet.Parsers;

namespace Jellyfin.Plugin.PhishNet.Tests.Parsers;

public class PhishFileNameParserTests
{
    private readonly Mock<ILogger<PhishFileNameParser>> _mockLogger;
    private readonly PhishFileNameParser _parser;

    public PhishFileNameParserTests()
    {
        _mockLogger = new Mock<ILogger<PhishFileNameParser>>();
        _parser = new PhishFileNameParser(_mockLogger.Object);
    }

    [Theory]
    [InlineData("ph2024-08-30.Webcast.1080p.hetyet.mkv", "2024-08-30", null, null, null)]
    [InlineData("Phish.2024-04-18.Las.Vegas.NV.1080p.WEB.x264.mkv", "2024-04-18", "Las Vegas", "NV", null)]
    [InlineData("phish2023-12-31.madison.square.garden.nyc.mkv", "2023-12-31", "New York City", "NY", "Madison Square Garden")]
    [InlineData("ph2024-07-21.Mondegreen.VT.sbd.flac", "2024-07-21", null, "VT", "Mondegreen")]
    public void Parse_ValidPhishDates_ShouldReturnCorrectParseResult(
        string filename, 
        string expectedDate, 
        string? expectedCity, 
        string? expectedState, 
        string? expectedVenue)
    {
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.ShowDate.Should().Be(DateTime.Parse(expectedDate));
        result.City.Should().Be(expectedCity);
        result.State.Should().Be(expectedState);
        result.Venue.Should().Be(expectedVenue);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Theory]
    [InlineData("Phish - 8-16-2024 - Mondegreen Secret Set.mp4", "2024-08-16", "Mondegreen", true, "Secret Set")]
    [InlineData("phish_2024-02-14_valentine_special.mkv", "2024-02-14", null, true, "Valentine Special")]
    [InlineData("Phish 2024.07.04 Independence Day Show.mp4", "2024-07-04", null, true, "Independence Day Show")]
    public void Parse_SpecialEvents_ShouldIdentifyCorrectly(
        string filename,
        string expectedDate,
        string? expectedVenue,
        bool expectedIsSpecialEvent,
        string expectedShowType)
    {
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.ShowDate.Should().Be(DateTime.Parse(expectedDate));
        result.Venue.Should().Be(expectedVenue);
        result.IsSpecialEvent.Should().Be(expectedIsSpecialEvent);
        result.ShowType.Should().Be(expectedShowType);
        result.Confidence.Should().BeGreaterThan(0.7);
    }

    [Theory]
    [InlineData("2024-08-30-phish-dicks-n1.mkv", "2024-08-30")]
    [InlineData("phish_08_30_2024_dicks.mp4", "2024-08-30")]
    [InlineData("08-30-24-phish-show.flac", "2024-08-30")]
    [InlineData("20240830-phish-concert.mkv", "2024-08-30")]
    public void Parse_VariousDateFormats_ShouldParseCorrectly(string filename, string expectedDate)
    {
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.ShowDate.Should().Be(DateTime.Parse(expectedDate));
        result.Confidence.Should().BeGreaterThan(0.3);
    }

    [Theory]
    [InlineData("random-music-file.mp3")]
    [InlineData("some-other-band-2024.mkv")]
    [InlineData("movie-from-2024.mp4")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NonPhishFiles_ShouldReturnLowConfidence(string filename)
    {
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.Confidence.Should().BeLessThan(0.3);
        result.ShowDate.Should().BeNull();
    }

    [Theory]
    [InlineData("ph2024-13-45.invalid.date.mkv")]
    [InlineData("phish-2024-02-30.impossible.mkv")]
    [InlineData("ph1982-01-01.before.band.mkv")] // Before Phish existed
    public void Parse_InvalidDates_ShouldReturnLowConfidence(string filename)
    {
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.Confidence.Should().BeLessThan(0.3);
        result.ShowDate.Should().BeNull();
    }

    [Fact]
    public void Parse_NullFilename_ShouldReturnEmptyResult()
    {
        // Act
        var result = _parser.Parse(null!, "/test/path");

        // Assert
        result.Confidence.Should().Be(0);
        result.ShowDate.Should().BeNull();
    }

    [Theory]
    [InlineData("Phish.1999.12.31.Big.Cypress.Millennium.mkv", "1999-12-31", true, "Millennium")]
    [InlineData("phish-2017-07-21-22-23-bakers-dozen-msg.mkv", "2017-07-21", true, "Baker's Dozen")]
    [InlineData("ph2019-08-30-31-09-01-dicks-run.mkv", "2019-08-30", false, null)]
    public void Parse_HistoricShows_ShouldParseCorrectly(
        string filename, 
        string expectedDate, 
        bool expectedIsSpecial, 
        string? expectedShowType)
    {
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.ShowDate.Should().Be(DateTime.Parse(expectedDate));
        result.IsSpecialEvent.Should().Be(expectedIsSpecial);
        result.ShowType.Should().Be(expectedShowType);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Theory]
    [InlineData("/media/phish/2024/ph2024-08-30.mkv")]
    [InlineData("/home/user/Music/Phish/Live/2024-08-30-show.flac")]
    [InlineData("C:\\Music\\Phish\\2024\\show.mkv")]
    public void Parse_VariousPaths_ShouldNotAffectParsing(string path)
    {
        var filename = Path.GetFileName(path);
        
        // Act
        var result = _parser.Parse(filename, path);

        // Assert
        result.Should().NotBeNull();
        // Results should be consistent regardless of path
    }

    [Theory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    [InlineData(".avi")]
    [InlineData(".flac")]
    [InlineData(".mp3")]
    [InlineData(".m4v")]
    public void Parse_SupportedExtensions_ShouldWork(string extension)
    {
        var filename = $"ph2024-08-30{extension}";
        
        // Act
        var result = _parser.Parse(filename, $"/test/{filename}");

        // Assert
        result.ShowDate.Should().Be(new DateTime(2024, 8, 30));
        result.Confidence.Should().BeGreaterThan(0.5);
    }
}