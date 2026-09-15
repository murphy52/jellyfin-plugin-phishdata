using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Parsers;
using Jellyfin.Plugin.PhishNet.Services;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Services;

/// <summary>
/// Collection metadata must come from API run detection, not only from a night number in the filename,
/// and must carry the full list of run dates so runs longer than two nights group correctly.
/// </summary>
public class PhishCollectionMetadataTests
{
    private static readonly DateTime Night3 = new(2024, 8, 31);
    private static readonly List<DateTime> DicksRun = new()
    {
        new DateTime(2024, 8, 29), new DateTime(2024, 8, 30), new DateTime(2024, 8, 31), new DateTime(2024, 9, 1)
    };

    [Fact]
    public void FromShow_ShouldUseApiRunInfo_WhenFilenameHasNoNightNumber()
    {
        var parse = new PhishShowParseResult { ShowDate = Night3, City = null, DayNumber = null };

        var meta = PhishCollectionMetadata.FromShow(parse, apiNightNumber: 3, apiRunDates: DicksRun, apiCity: "Commerce City");

        meta.Should().NotBeNull();
        meta!.City.Should().Be("Commerce City");
        meta.Year.Should().Be(2024);
        meta.DayNumber.Should().Be(3);
        meta.ShowDate.Should().Be(Night3);
        meta.RunDates.Should().Equal(DicksRun);
    }

    [Fact]
    public void FromShow_ShouldFallBackToParsedNightNumber_WhenApiHasNoRun()
    {
        var parse = new PhishShowParseResult { ShowDate = Night3, City = "Commerce City", DayNumber = 3 };

        var meta = PhishCollectionMetadata.FromShow(parse, apiNightNumber: null, apiRunDates: null, apiCity: null);

        meta.Should().NotBeNull();
        meta!.DayNumber.Should().Be(3);
        // Fallback run spans from night 1 through this show
        meta.RunDates.Should().Equal(new DateTime(2024, 8, 29), new DateTime(2024, 8, 30), Night3);
    }

    [Fact]
    public void FromShow_ShouldReturnNull_WhenNothingIndicatesARun()
    {
        var parse = new PhishShowParseResult { ShowDate = Night3, DayNumber = null };

        PhishCollectionMetadata.FromShow(parse, null, null, "Commerce City").Should().BeNull();
    }

    [Fact]
    public void ApplyTo_And_FromProviderIds_ShouldRoundTrip()
    {
        var parse = new PhishShowParseResult { ShowDate = Night3 };
        var meta = PhishCollectionMetadata.FromShow(parse, 3, DicksRun, "Commerce City")!;
        var movie = new Movie();

        meta.ApplyTo(movie);
        var restored = PhishCollectionMetadata.FromProviderIds(movie.ProviderIds);

        restored.Should().NotBeNull();
        restored!.City.Should().Be("Commerce City");
        restored.Year.Should().Be(2024);
        restored.DayNumber.Should().Be(3);
        restored.ShowDate.Should().Be(Night3);
        restored.RunDates.Should().Equal(DicksRun);
    }

    [Fact]
    public void FromProviderIds_ShouldHandleLegacyIdsWithoutRunDates()
    {
        var movie = new Movie();
        movie.ProviderIds["PhishCollectionCity"] = "Commerce City";
        movie.ProviderIds["PhishCollectionYear"] = "2024";
        movie.ProviderIds["PhishCollectionDayNumber"] = "2";
        movie.ProviderIds["PhishCollectionDate"] = "2024-08-30";

        var meta = PhishCollectionMetadata.FromProviderIds(movie.ProviderIds);

        meta.Should().NotBeNull();
        meta!.RunDates.Should().Equal(new DateTime(2024, 8, 29), new DateTime(2024, 8, 30));
    }

    [Fact]
    public void FromProviderIds_ShouldReturnNull_WhenIdsMissing()
    {
        PhishCollectionMetadata.FromProviderIds(new Movie().ProviderIds).Should().BeNull();
        PhishCollectionMetadata.FromProviderIds(null).Should().BeNull();
    }
}
