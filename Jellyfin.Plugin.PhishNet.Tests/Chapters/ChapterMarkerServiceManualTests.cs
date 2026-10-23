using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The manual path (plan §4.4, phase 4): the <c>.chapters.txt</c> sidecar, and the admin's Retry,
/// Apply anyway and Ignore.
/// </summary>
public partial class ChapterMarkerServiceTests
{
    private const string ThreeSongs = "0:00:00 Chalk Dust Torture\n0:05:00 Tweezer\n0:10:30 Harry Hood\n";

    private static readonly List<AlignedChapter> ReviewRows =
        [new AlignedChapter("Chalk Dust Torture", 60, 0.31), new AlignedChapter("Tweezer", 420, 0.22), new AlignedChapter("Harry Hood", 900, null)];

    [Fact]
    public async Task Process_ShouldApplyASidecar_WithoutReadingTheVideoOrPhishIn()
    {
        WriteSidecar(ThreeSongs);

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Applied);
        _db.Chapters.Select(c => (c.Name, c.StartPositionTicks)).Should().Equal(
            ("Chalk Dust Torture", 0L),
            ("Tweezer", TimeSpan.FromMinutes(5).Ticks),
            ("Harry Hood", TimeSpan.FromSeconds(630).Ticks));
        _decoder.FileDecodes.Should().Be(0);
        _audio.ShowRequests.Should().BeEmpty();
        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Applied);
        state.Source.Should().Be(ChapterSource.Sidecar);
        state.Fingerprint!.SidecarHash.Should().NotBeNullOrEmpty();
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);
    }

    [Fact]
    public async Task Process_ShouldReplaceAlignedChapters_WhenASidecarIsAddedLater()
    {
        await Process();
        WriteSidecar(ThreeSongs);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "the sidecar is checked before the Applied fast path");

        _db.Chapters.Select(c => c.Name).Should().Equal("Chalk Dust Torture", "Tweezer", "Harry Hood");
        _decoder.FileDecodes.Should().Be(1);
    }

    [Fact]
    public async Task Process_ShouldApplyAnEditedSidecar()
    {
        WriteSidecar(ThreeSongs);
        await Process();

        WriteSidecar("0:00:00 Chalk Dust Torture\n0:06:00 Tweezer\n");
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);

        _db.Chapters.Select(c => c.StartPositionTicks).Should().Equal(0L, TimeSpan.FromMinutes(6).Ticks);
    }

    [Fact]
    public async Task Process_ShouldSendABadSidecarToReview_WithItsLineNumber_AndNotAlign()
    {
        WriteSidecar("0:00:00 Chalk Dust Torture\nTweezer at five minutes\n");

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.NeedsReview);
        outcome.Reason.Should().StartWith("sidecar line 2:");
        _db.Saves.Should().Be(0);
        _decoder.FileDecodes.Should().Be(0);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);

        WriteSidecar(ThreeSongs);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "fixing the sidecar is a change");
    }

    [Fact]
    public async Task Process_ShouldSendToReview_ASidecarThatRunsPastTheEndOfTheVideo()
    {
        WriteSidecar("0:00:00 Chalk Dust Torture\n9:00:00 Tweezer\n");

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.NeedsReview);
        outcome.Reason.Should().StartWith("sidecar: ").And.Contain("past the end");
        _db.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldLeaveOtherRealChaptersAlone_EvenWithASidecar()
    {
        _db.Chapters = [new ChapterInfo { StartPositionTicks = 0, Name = "Intro" }];
        WriteSidecar(ThreeSongs);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Skipped);

        _db.Saves.Should().Be(0);
        (await Record())!.Reason.Should().Be(ChapterMarkerService.HasChaptersReason);
    }

    [Fact]
    public async Task Process_ShouldTakeChaptersEqualToTheSidecar_AsApplied_WhenTheRecordWasLost()
    {
        WriteSidecar(ThreeSongs);
        await Process();
        File.Delete(Path.Combine(_dir, "state", _movie.Id.ToString("N") + ".json"));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
        (await Record())!.Source.Should().Be(ChapterSource.Sidecar);
    }

    [Fact]
    public async Task Process_ShouldAlignAgain_WhenTheSidecarIsRemoved()
    {
        WriteSidecar(ThreeSongs);
        await Process();
        File.Delete(SidecarChapterReader.PathFor(_movie.Path));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "the sidecar's chapters are ours to replace");

        _db.Chapters.Should().HaveCount(12);
        (await Record())!.Source.Should().Be(ChapterSource.Aligned);
    }

    [Fact]
    public async Task Process_ShouldReapplyASidecar_AfterJellyfinDropsIt()
    {
        WriteSidecar(ThreeSongs);
        await Process();
        _db.Chapters.Clear();

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Reapplied);

        _db.Chapters.Should().HaveCount(3);
    }

    [Fact]
    public async Task Process_ShouldApplyASidecarAddedToAnIgnoredItem()
    {
        await _service.Store.WriteAsync(_movie.Id, new ChapterState { Status = ChapterStatus.Ignored, Fingerprint = FileFingerprint.Of(_movie.Path) }, CancellationToken.None);
        WriteSidecar(ThreeSongs);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Process_ShouldStillKnowItsOwnChapters_WhenASwappedFileWaitsForPhishIn()
    {
        await Process();
        File.WriteAllBytes(_movie.Path, new byte[2000]);
        File.SetLastWriteTimeUtc(_movie.Path, _time.Now - TimeSpan.FromHours(1));
        _audio.Status = PhishInShowStatus.NotFound;
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Pending);
        (await Record())!.SavedChapters.Should().HaveCount(12);

        _audio.Status = PhishInShowStatus.Ok;
        _time.Advance(TimeSpan.FromHours(2));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "the chapters in Jellyfin are still the ones we saved");
    }

    [Fact]
    public async Task Retry_ShouldRealignAReviewItem_OnTheNextPass()
    {
        await WriteReviewRecord();
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);

        (await _service.RetryAsync(_movie.Id, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Done);
        (await Record())!.RetryRequested.Should().BeTrue();

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
        (await Record())!.RetryRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Retry_ShouldRealignAnAppliedShow_AndReplaceOurChapters()
    {
        await Process();

        await _service.RetryAsync(_movie.Id, CancellationToken.None);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
        _decoder.FileDecodes.Should().Be(2);
        _db.Saves.Should().Be(2);
    }

    [Fact]
    public async Task Retry_ShouldKeepOurChapters_WhenTheRetryHasToWaitForPhishIn()
    {
        await Process();
        await _service.RetryAsync(_movie.Id, CancellationToken.None);
        _audio.Status = PhishInShowStatus.NotFound;
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Pending);

        _audio.Status = PhishInShowStatus.Ok;
        _time.Advance(TimeSpan.FromHours(2));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Retry_ShouldSkipTheBackoff_AndClearIgnored()
    {
        _audio.Status = PhishInShowStatus.NotFound;
        await Process();
        _audio.Status = PhishInShowStatus.Ok;
        await _service.IgnoreAsync(_movie.Id, CancellationToken.None);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);

        await _service.RetryAsync(_movie.Id, CancellationToken.None);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "a retry doesn't wait for the hour-long backoff");
    }

    [Fact]
    public async Task Actions_ShouldReportNotFound_ForAnItemWithNoRecord()
    {
        (await _service.RetryAsync(_movie.Id, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.NotFound);
        (await _service.IgnoreAsync(_movie.Id, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.NotFound);
        (await _service.ApplyAnywayAsync(_movie, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.NotFound);
    }

    [Fact]
    public async Task Ignore_ShouldSettleAReviewItem_AndKeepItsReason()
    {
        await WriteReviewRecord();

        (await _service.IgnoreAsync(_movie.Id, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Done);

        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Ignored);
        state.Reason.Should().Be("low-confidence (median 0.31)");
        _time.Advance(TimeSpan.FromDays(400));
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);
        _decoder.FileDecodes.Should().Be(0);
    }

    [Fact]
    public async Task Ignore_ShouldRefuseAnAppliedShow()
    {
        await Process();

        var result = await _service.IgnoreAsync(_movie.Id, CancellationToken.None);

        result.Status.Should().Be(ChapterActionStatus.Conflict);
        (await Record())!.Status.Should().Be(ChapterStatus.Applied);
    }

    [Fact]
    public async Task ApplyAnyway_ShouldSaveTheReviewRows()
    {
        await WriteReviewRecord();

        (await _service.ApplyAnywayAsync(_movie, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Done);

        _db.Chapters.Select(c => c.Name).Should().Equal("Chalk Dust Torture", "Tweezer", "Harry Hood");
        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Applied);
        state.Source.Should().Be(ChapterSource.ApplyAnyway);
        state.MedianConfidence.Should().Be(0.31);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);
        _db.Chapters.Clear();
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Reapplied, "applied anyway is applied");
    }

    [Fact]
    public async Task ApplyAnyway_ShouldReplaceDummyChapters_AndOurOwnOldOnes()
    {
        _server.DummyChapterDuration = 300;
        _db.Chapters = DummySet();
        await WriteReviewRecord();

        (await _service.ApplyAnywayAsync(_movie, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Done);
        _db.Chapters.Should().HaveCount(3);
    }

    [Fact]
    public async Task ApplyAnyway_ShouldRefuse_AReviewItemWithNoRows()
    {
        await WriteReviewRecord(rows: []);

        var result = await _service.ApplyAnywayAsync(_movie, CancellationToken.None);

        result.Status.Should().Be(ChapterActionStatus.Conflict);
        _db.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAnyway_ShouldRefuse_AnItemThatIsNotInReview()
    {
        await Process();

        (await _service.ApplyAnywayAsync(_movie, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Conflict);
        _db.Saves.Should().Be(1);
    }

    [Fact]
    public async Task ApplyAnyway_ShouldNeverSkipTheStructuralChecks()
    {
        await WriteReviewRecord(rows: [new AlignedChapter("Chalk Dust Torture", 60, 0.3), new AlignedChapter("Tweezer", 40, 0.3)]);

        var result = await _service.ApplyAnywayAsync(_movie, CancellationToken.None);

        result.Status.Should().Be(ChapterActionStatus.Conflict);
        result.Message.Should().Contain("not after the chapter before it");
        _db.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAnyway_ShouldRefuse_WhenTheFileChangedSinceTheAlignment()
    {
        await WriteReviewRecord();
        File.WriteAllBytes(_movie.Path, new byte[2000]);

        var result = await _service.ApplyAnywayAsync(_movie, CancellationToken.None);

        result.Status.Should().Be(ChapterActionStatus.Conflict);
        result.Message.Should().Contain("Retry");
        _db.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAnyway_ShouldNeverReplaceOtherRealChapters()
    {
        _db.Chapters = [new ChapterInfo { StartPositionTicks = 0, Name = "Intro" }];
        await WriteReviewRecord();

        var result = await _service.ApplyAnywayAsync(_movie, CancellationToken.None);

        result.Status.Should().Be(ChapterActionStatus.Conflict);
        _db.Chapters.Single().Name.Should().Be("Intro");
    }

    [Fact]
    public async Task Actions_ShouldReportBusy_WhileTheItemIsBeingProcessed()
    {
        await WriteReviewRecord(algorithmVersion: 0);
        var release = new TaskCompletionSource();
        _decoder.Gate = release.Task;
        var running = Process();
        await _decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        (await _service.RetryAsync(_movie.Id, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Busy);
        (await _service.IgnoreAsync(_movie.Id, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Busy);
        (await _service.ApplyAnywayAsync(_movie, CancellationToken.None)).Status.Should().Be(ChapterActionStatus.Busy);

        release.SetResult();
        (await running).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    private void WriteSidecar(string text) => File.WriteAllText(SidecarChapterReader.PathFor(_movie.Path), text);

    private Task WriteReviewRecord(List<AlignedChapter>? rows = null, int algorithmVersion = ChapterMarkerService.AlgorithmVersion) =>
        _service.Store.WriteAsync(
            _movie.Id,
            new ChapterState
            {
                Status = ChapterStatus.NeedsReview,
                Reason = "low-confidence (median 0.31)",
                AlgorithmVersion = algorithmVersion,
                Fingerprint = FileFingerprint.Of(_movie.Path),
                ShowDate = new DateTime(2026, 7, 7),
                Chapters = rows ?? ReviewRows,
                MedianConfidence = 0.31,
                LowConfidenceCount = 2,
            },
            CancellationToken.None);
}
