using System.Text;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

public class SidecarChapterReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sidecar-" + Guid.NewGuid().ToString("N"));

    public SidecarChapterReaderTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void PathFor_ShouldSitNextToTheVideo_WithTheVideosBaseName()
    {
        SidecarChapterReader.PathFor(Path.Combine("/media", "Phish", "ph2026-07-07.Webcast.1080p.mkv"))
            .Should().Be(Path.Combine("/media", "Phish", "ph2026-07-07.Webcast.1080p.chapters.txt"));
    }

    [Fact]
    public void Parse_ShouldReadOneChapterPerLine()
    {
        var result = Parse("0:00:00 Set 1 opener\n0:07:31 Tweezer\n1:02:03.5 Harry Hood\n");

        result.Status.Should().Be(SidecarStatus.Ok);
        result.Chapters.Select(c => (c.Title, c.StartSeconds)).Should().Equal(
            ("Set 1 opener", 0.0),
            ("Tweezer", 451.0),
            ("Harry Hood", 3723.5));
        result.Chapters.Should().OnlyContain(c => c.Confidence == null);
        result.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_ShouldAcceptMinutesAndSeconds_ABomCrlfBlankLinesAndComments()
    {
        var result = Parse("﻿# Phish 2026-07-07\r\n\r\n00:00 Chalk Dust Torture\r\n12:05   Sand  \r\n1:00:00 Tweezer\r\n");

        result.Status.Should().Be(SidecarStatus.Ok, result.Error);
        result.Chapters.Select(c => (c.Title, c.StartSeconds)).Should().Equal(
            ("Chalk Dust Torture", 0.0),
            ("Sand", 725.0),
            ("Tweezer", 3600.0));
    }

    [Theory]
    [InlineData("0:00:00 Opener\nTweezer\n", 2, "expected")]
    [InlineData("0:00:00 Opener\n0:61:00 Tweezer\n", 2, "expected")]
    [InlineData("0:00:00 Opener\n0:05:60 Tweezer\n", 2, "expected")]
    [InlineData("0:00:00\n", 1, "expected")]
    [InlineData("0:00:00 Opener\n0:10:00 Tweezer\n0:09:00 Sand\n", 3, "not after")]
    [InlineData("0:00:00 Opener\n0:00:00 Tweezer\n", 2, "not after")]
    [InlineData("-0:01:00 Opener\n", 1, "expected")]
    public void Parse_ShouldRejectTheWholeFile_AtTheFirstBadLine(string text, int line, string why)
    {
        var result = Parse(text);

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Chapters.Should().BeEmpty();
        result.Error.Should().StartWith($"sidecar line {line}:").And.Contain(why);
        result.Hash.Should().NotBeNullOrEmpty("an edit to a bad sidecar must still count as a change");
    }

    [Fact]
    public void Parse_ShouldRejectAnOverlongTitle()
    {
        var result = Parse("0:00:00 " + new string('x', ChapterValidator.MaxTitleLength + 1) + "\n");

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().StartWith("sidecar line 1:").And.Contain("longer than");
    }

    [Fact]
    public void Parse_ShouldRejectAFileWithNoChapters()
    {
        var result = Parse("# nothing yet\n\n");

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().Be("sidecar has no chapters");
    }

    [Fact]
    public void Parse_ShouldRejectTooManyChapters()
    {
        var text = string.Concat(Enumerable.Range(0, ChapterValidator.MaxChapters + 1).Select(i => $"0:{i / 60:00}:{i % 60:00} Song {i}\n"));

        var result = Parse(text);

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().Contain("more than " + ChapterValidator.MaxChapters);
    }

    [Fact]
    public void Parse_ShouldRejectTextThatIsNotUtf8()
    {
        var result = SidecarChapterReader.Parse(new byte[] { 0x30, 0x3A, 0x30, 0x30, 0x20, 0xFF, 0xFE, 0x0A });

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().Contain("UTF-8");
    }

    [Fact]
    public void Parse_ShouldHashTheBytes_SoAnyEditChangesTheHash()
    {
        var a = Parse("0:00:00 Tweezer\n");
        var b = Parse("0:00:00 Tweezer\n");
        var c = Parse("0:00:01 Tweezer\n");

        a.Hash.Should().Be(b.Hash);
        a.Hash.Should().NotBe(c.Hash);
    }

    [Fact]
    public void Read_ShouldReportMissing_WhenThereIsNoSidecar()
    {
        var result = SidecarChapterReader.Read(Path.Combine(_dir, "ph2026-07-07.mkv"));

        result.Status.Should().Be(SidecarStatus.Missing);
        result.Hash.Should().BeNull();
    }

    [Fact]
    public void Read_ShouldReadTheFileNextToTheVideo()
    {
        var video = Path.Combine(_dir, "ph2026-07-07.mkv");
        File.WriteAllText(SidecarChapterReader.PathFor(video), "0:00:00 Tweezer\n0:20:00 Sand\n");

        var result = SidecarChapterReader.Read(video);

        result.Status.Should().Be(SidecarStatus.Ok);
        result.Chapters.Should().HaveCount(2);
        result.Hash.Should().Be(Parse("0:00:00 Tweezer\n0:20:00 Sand\n").Hash);
    }

    [Fact]
    public void Read_ShouldRejectAnOversizedSidecar_WithoutParsingIt()
    {
        var video = Path.Combine(_dir, "ph2026-07-07.mkv");
        File.WriteAllBytes(SidecarChapterReader.PathFor(video), new byte[SidecarChapterReader.MaxBytes + 1]);

        var result = SidecarChapterReader.Read(video);

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().Contain("larger than");
        result.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_ShouldRejectNonAsciiDigits_InsteadOfThrowing()
    {
        var result = Parse("\uFF10:\uFF10\uFF15 Tweezer\n");

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().StartWith("sidecar line 1:");
    }

    [Fact]
    public void Parse_ShouldSplitOldMacLineEndings_IntoSeparateChapters()
    {
        var result = Parse("0:00 Intro\r5:00 Tweezer\r10:00 Sand\r");

        result.Status.Should().Be(SidecarStatus.Ok, result.Error);
        result.Chapters.Select(c => c.Title).Should().Equal("Intro", "Tweezer", "Sand");
    }

    [Fact]
    public void Read_ShouldRefuseASymlinkedSidecar_WithoutFollowingIt()
    {
        var video = Path.Combine(_dir, "ph2026-07-07.mkv");
        var target = Path.Combine(_dir, "elsewhere.txt");
        File.WriteAllText(target, "secret first line\n");
        File.CreateSymbolicLink(SidecarChapterReader.PathFor(video), target);

        var result = SidecarChapterReader.Read(video);

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().Contain("link").And.NotContain("secret");
        result.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Read_ShouldNotOpenAnEmptyOrSpecialFile()
    {
        var video = Path.Combine(_dir, "ph2026-07-07.mkv");
        File.WriteAllBytes(SidecarChapterReader.PathFor(video), Array.Empty<byte>());

        var result = SidecarChapterReader.Read(video);

        result.Status.Should().Be(SidecarStatus.Invalid);
        result.Error.Should().Be("sidecar has no chapters");
    }

    private static SidecarResult Parse(string text) => SidecarChapterReader.Parse(Encoding.UTF8.GetBytes(text));
}
