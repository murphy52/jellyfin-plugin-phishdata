using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The plugin only fills in chapters that are missing or Jellyfin's dummies. Real chapters from the
/// file, the NAS fleet or a person are never replaced, because nothing in ChapterInfo says who made them.
/// </summary>
public class ChapterDetectorTests
{
    private const string NameFormat = "Chapter {0}";
    private static readonly long Runtime = TimeSpan.FromSeconds(1000).Ticks;
    private static readonly DummySignature Dummies = new(Runtime, DurationSeconds: 300, NameFormat);

    private static readonly List<AlignedChapter> Ours =
    [
        new("Chalk Dust Torture", 12.5, 0.8),
        new("Ghost", 604.25, 0.7),
    ];

    [Fact]
    public void NoChapters_ShouldBeMissing()
    {
        ChapterDetector.Classify([], Ours, Dummies).Should().Be(ChapterSituation.Missing);
        ChapterDetector.Classify([], null, null).Should().Be(ChapterSituation.Missing);
    }

    [Fact]
    public void JellyfinsDummySet_ShouldBeDummy()
    {
        // 1000 s at 300 s per dummy: Jellyfin makes 3, at 0, 300 and 600 s.
        ChapterDetector.Classify(DummySet(3), null, Dummies).Should().Be(ChapterSituation.Dummy);
    }

    [Fact]
    public void ASingleDummy_ShouldBeDummy_WhenTheVideoIsShorterThanOneDummy()
    {
        var shortVideo = Dummies with { RuntimeTicks = TimeSpan.FromSeconds(200).Ticks };

        ChapterDetector.Classify(DummySet(1), null, shortVideo).Should().Be(ChapterSituation.Dummy);
    }

    [Fact]
    public void ASingleRealChapter_ShouldBeOther()
    {
        // A file's one chapter, renamed "Chapter 1" by NormalizeChapterNames, isn't a dummy set for a long video.
        ChapterDetector.Classify(DummySet(1), null, Dummies).Should().Be(ChapterSituation.Other);
    }

    [Theory]
    [InlineData(2)] // one short of what Jellyfin makes now
    [InlineData(4)]
    public void ADummySetOfTheWrongSize_ShouldBeOther(int count)
    {
        ChapterDetector.Classify(DummySet(count), null, Dummies).Should().Be(ChapterSituation.Other);
    }

    [Fact]
    public void ADummyLookalikeWithAMovedStart_ShouldBeOther()
    {
        var set = DummySet(3);
        set[2].StartPositionTicks += 1;

        ChapterDetector.Classify(set, null, Dummies).Should().Be(ChapterSituation.Other);
    }

    [Fact]
    public void ADummyLookalikeWithARealName_ShouldBeOther()
    {
        var set = DummySet(3);
        set[1].Name = "Ghost";

        ChapterDetector.Classify(set, null, Dummies).Should().Be(ChapterSituation.Other);
    }

    [Fact]
    public void Dummies_ShouldBeOther_WhenReplacingThemIsOff()
    {
        ChapterDetector.Classify(DummySet(3), null, null).Should().Be(ChapterSituation.Other);
    }

    [Fact]
    public void OurOwnSavedChapters_ShouldBeOurs()
    {
        ChapterDetector.Classify(ChapterDetector.ToChapterInfos(Ours), Ours, Dummies).Should().Be(ChapterSituation.Ours);
    }

    [Fact]
    public void ChaptersSomeoneEdited_ShouldBeOther()
    {
        var edited = ChapterDetector.ToChapterInfos(Ours);
        edited[1].Name = "Ghost >";

        ChapterDetector.Classify(edited, Ours, Dummies).Should().Be(ChapterSituation.Other);
    }

    [Fact]
    public void RealChapters_ShouldBeOther_WithOrWithoutACachedResult()
    {
        var file = new List<ChapterInfo> { new() { StartPositionTicks = 0, Name = "Set 1" }, new() { StartPositionTicks = 5_000_000_000, Name = "Set 2" } };

        ChapterDetector.Classify(file, Ours, Dummies).Should().Be(ChapterSituation.Other);
        ChapterDetector.Classify(file, null, Dummies).Should().Be(ChapterSituation.Other);
    }

    [Fact]
    public void ToChapterInfos_ShouldRoundStartsToTicks()
    {
        var infos = ChapterDetector.ToChapterInfos(Ours);

        infos.Select(c => c.StartPositionTicks).Should().Equal(125_000_000L, 6_042_500_000L);
        infos.Select(c => c.Name).Should().Equal("Chalk Dust Torture", "Ghost");
    }

    private static List<ChapterInfo> DummySet(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ChapterInfo { StartPositionTicks = TimeSpan.FromSeconds(300 * i).Ticks, Name = $"Chapter {i + 1}" })
            .ToList();
}
