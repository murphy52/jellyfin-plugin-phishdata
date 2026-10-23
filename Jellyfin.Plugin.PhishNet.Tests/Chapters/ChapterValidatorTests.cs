using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// Jellyfin's SaveChapters doesn't sort or check rows, so every list is checked before it is saved,
/// whether it came from the aligner, Apply anyway or a sidecar.
/// </summary>
public class ChapterValidatorTests
{
    private static readonly long Runtime = TimeSpan.FromHours(3).Ticks;

    [Fact]
    public void Validate_ShouldAcceptIncreasingStartsInsideTheRuntime()
    {
        ChapterValidator.Validate(Rows((0, "Chalk Dust Torture"), (604, "Ghost"), (10_799, "Tweezer Reprise")), Runtime)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(-1.0, "before the start")]
    [InlineData(10_800.0, "past the end")]
    [InlineData(double.NaN, "not a number")]
    [InlineData(double.PositiveInfinity, "not a number")]
    public void Validate_ShouldRejectAStartOutsideTheRuntime(double start, string reason)
    {
        ChapterValidator.Validate(Rows((0, "Ghost"), (start, "Tweezer")), Runtime).Should().Contain(reason);
    }

    [Theory]
    [InlineData(600.0)]
    [InlineData(500.0)]
    public void Validate_ShouldRejectStartsThatDoNotIncrease(double second)
    {
        ChapterValidator.Validate(Rows((600, "Ghost"), (second, "Tweezer")), Runtime)
            .Should().Contain("chapter 2 'Tweezer'").And.Contain("not after");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectAnEmptyTitle(string title)
    {
        ChapterValidator.Validate(Rows((0, "Ghost"), (60, title)), Runtime).Should().Contain("chapter 2").And.Contain("no title");
    }

    [Fact]
    public void Validate_ShouldRejectAnOverlongTitle()
    {
        ChapterValidator.Validate(Rows((0, new string('x', 201))), Runtime).Should().Contain("longer than 200");
        ChapterValidator.Validate(Rows((0, new string('x', 200))), Runtime).Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldWantOneToOneHundredChapters()
    {
        ChapterValidator.Validate([], Runtime).Should().Contain("no chapters");
        var many = Enumerable.Range(0, 101).Select(i => new AlignedChapter($"Song {i}", i * 60, 0.7)).ToList();
        ChapterValidator.Validate(many, Runtime).Should().Contain("101 chapters");
        ChapterValidator.Validate(many[..100], Runtime).Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldRejectEverything_WhenTheRuntimeIsUnknown()
    {
        ChapterValidator.Validate(Rows((0, "Ghost")), 0).Should().Contain("runtime");
    }

    private static List<AlignedChapter> Rows(params (double Start, string Title)[] rows) =>
        rows.Select(r => new AlignedChapter(r.Title, r.Start, 0.7)).ToList();
}
