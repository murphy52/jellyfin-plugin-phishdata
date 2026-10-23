using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Configuration;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Configuration;

public class PluginConfigurationTests
{
    [Fact]
    public void ChapterMarkers_ShouldBeOptIn_WithThePlannedDefaults()
    {
        var config = new PluginConfiguration();

        config.EnableChapterMarkers.Should().BeFalse("the feature reads whole videos and downloads from phish.in");
        config.ChapterMarkersOnLibraryChanges.Should().BeTrue();
        config.ChapterMarkersMaxShowsPerRun.Should().Be(10);
        config.ChapterMarkersMaxGbPerRun.Should().Be(100);
        config.ReplaceDummyChapters.Should().BeTrue();
    }
}
