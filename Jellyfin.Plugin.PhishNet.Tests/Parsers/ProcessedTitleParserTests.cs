using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Parsers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Parsers;

public class ProcessedTitleParserTests
{
    private readonly PhishFileNameParser _parser = new(Mock.Of<ILogger<PhishFileNameParser>>());

    [Theory]
    [InlineData("N1 Phish 7-22-2026", 1, "2026-07-22", null)]
    [InlineData("N2 Phish Commerce City 8-30-2024", 2, "2024-08-30", "Commerce City")]
    [InlineData("N3 Phish New York 7-25-2026", 3, "2026-07-25", "New York")]
    public void Parse_ProcessedTitleWithNightIndicator_ShouldCaptureNightAndOptionalCity(string title, int night, string date, string? city)
    {
        var result = _parser.Parse(title);

        result.DayNumber.Should().Be(night);
        result.ShowDate.Should().Be(DateTime.Parse(date));
        result.City.Should().Be(city);
        result.Confidence.Should().BeGreaterOrEqualTo(0.8);
    }
}
