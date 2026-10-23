using System.Globalization;
using System.Net;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The service's rules with everything outside it faked: Jellyfin's chapter table, phish.in, ffmpeg and
/// the clock. The alignment itself is real, on a synthetic show.
/// </summary>
public class ChapterMarkerServiceTests : IDisposable
{
    private static readonly ConsensusAlignerTests.SyntheticShow Synthetic =
        ConsensusAlignerTests.SyntheticShow.Build(seed: 10, songs: 12, leadInSec: 60, drift: 1.02, setbreakAfter: null, setbreakSec: 0);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chapter-service-" + Guid.NewGuid().ToString("N"));
    private readonly FakeChapters _db = new();
    private readonly FakeAudio _audio = new();
    private readonly FakeDecoder _decoder = new();
    private readonly TestClock _time = new(new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc));
    private readonly ServerConfiguration _server = new() { DummyChapterDuration = 0 };
    private readonly PluginConfiguration _config = new();
    private readonly ChapterMarkerService _service;
    private readonly Movie _movie;

    public ChapterMarkerServiceTests()
    {
        var folder = Path.Combine(_dir, "media", "Phish");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ph2026-07-07.Webcast.1080p.mkv");
        File.WriteAllBytes(path, new byte[1000]);
        File.SetLastWriteTimeUtc(path, _time.Now - TimeSpan.FromHours(1));
        _movie = new Movie
        {
            Id = Guid.NewGuid(),
            Path = path,
            Name = "ph2026-07-07.Webcast.1080p",
            RunTimeTicks = TimeSpan.FromSeconds(Synthetic.Video.Length / (double)AlignmentConstants.Fps).Ticks,
        };

        var serverConfig = new Mock<IServerConfigurationManager>();
        serverConfig.Setup(c => c.Configuration).Returns(_server);
        var localization = new Mock<ILocalizationManager>();
        localization.Setup(l => l.GetLocalizedString("ChapterNameValue")).Returns("Chapter {0}");
        _service = new ChapterMarkerService(
            _db.Object,
            serverConfig.Object,
            localization.Object,
            _audio,
            _decoder,
            new ChapterStateStore(Path.Combine(_dir, "state")),
            () => _config,
            _time,
            NullLogger<ChapterMarkerService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Process_ShouldAlignSaveAndRecord_ANewShow()
    {
        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Applied);
        _db.Chapters.Should().HaveCount(12);
        for (int i = 0; i < 12; i++)
        {
            (_db.Chapters[i].StartPositionTicks / (double)TimeSpan.TicksPerSecond).Should().BeApproximately(Synthetic.TrueStarts[i], 2.0);
            _db.Chapters[i].Name.Should().Be($"Song {i + 1}");
        }

        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Applied);
        state.AlgorithmVersion.Should().Be(ChapterMarkerService.AlgorithmVersion);
        state.Fingerprint.Should().Be(FileFingerprint.Of(_movie.Path));
        state.ShowDate.Should().Be(new DateTime(2026, 7, 7));
        state.Chapters.Should().HaveCount(12);
        state.MedianConfidence.Should().BeGreaterThan(0.8);
        _audio.ShowRequests.Should().Equal(new DateTime(2026, 7, 7));
    }

    [Fact]
    public async Task Process_ShouldNeverRealignAnAppliedShow()
    {
        await Process();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Unchanged);
        _decoder.FileDecodes.Should().Be(1);
        _db.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Process_ShouldReapplyTheCachedChapters_AfterJellyfinDropsThem()
    {
        await Process();
        var saved = _db.Chapters.ToList();
        _db.Chapters.Clear();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Reapplied);
        _db.Chapters.Should().BeEquivalentTo(saved);
        _decoder.FileDecodes.Should().Be(1, "a re-apply reads nothing");
        _audio.ShowRequests.Should().HaveCount(1, "a re-apply downloads nothing");
    }

    [Fact]
    public async Task Process_ShouldReapplyOverJellyfinsDummyChapters()
    {
        await Process();
        _server.DummyChapterDuration = 300;
        _db.Chapters = DummySet();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Reapplied);
        _db.Chapters.Should().HaveCount(12);
    }

    [Fact]
    public async Task Process_ShouldLeaveDummyChaptersAlone_WhenReplacingThemIsOff()
    {
        _server.DummyChapterDuration = 300;
        _config.ReplaceDummyChapters = false;
        _db.Chapters = DummySet();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Skipped);
        outcome.Reason.Should().Be(ChapterMarkerService.HasChaptersReason);
        _db.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldAlignOverDummyChapters_WhenTheShowHasNoRecord()
    {
        _server.DummyChapterDuration = 300;
        _db.Chapters = DummySet();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Applied);
        _db.Chapters.Should().HaveCount(12);
    }

    [Fact]
    public async Task Process_ShouldMarkSuperseded_AndLeaveOtherChaptersAlone()
    {
        await Process();
        _db.Chapters = [new ChapterInfo { StartPositionTicks = 0, Name = "Set 1" }, new ChapterInfo { StartPositionTicks = TimeSpan.FromHours(1).Ticks, Name = "Set 2" }];
        var theirs = _db.Chapters.ToList();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Superseded);
        _db.Chapters.Should().BeEquivalentTo(theirs);
        (await Record())!.Status.Should().Be(ChapterStatus.Superseded);

        _db.Chapters.Clear();
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Reapplied, "a superseded show gets ours back once theirs are gone");
    }

    [Fact]
    public async Task Process_ShouldSkipWithoutReading_AShowThatAlreadyHasRealChapters()
    {
        _db.Chapters = [new ChapterInfo { StartPositionTicks = 0, Name = "Intro" }];

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Skipped);
        _decoder.FileDecodes.Should().Be(0);
        _db.Saves.Should().Be(0);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);
    }

    [Theory]
    [InlineData("Some Other Band 2026-07-07", "/media/Movies/Some Other Band 2026-07-07.mkv")]
    [InlineData("Phish Documentary", "/media/Phish/Phish Documentary.mkv")]
    public async Task Process_ShouldIgnoreItemsThatAreNotDatedPhishShows(string name, string path)
    {
        var item = new Movie { Id = Guid.NewGuid(), Name = name, Path = path, RunTimeTicks = TimeSpan.FromHours(3).Ticks };

        var outcome = await _service.ProcessAsync(item, null, CancellationToken.None);

        outcome.Kind.Should().Be(ChapterOutcomeKind.NotEligible);
        (await _service.Store.ReadAsync(item.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldWait_UntilJellyfinHasProbedTheFile()
    {
        _movie.RunTimeTicks = null;

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.NotEligible);
        (await Record()).Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldSendToReview_WhenThePremiereDateDisagreesWithTheFileName()
    {
        _movie.PremiereDate = new DateTime(2026, 7, 8);

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.NeedsReview);
        outcome.Reason.Should().StartWith(ChapterMarkerService.DateMismatchReason);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);
        _audio.ShowRequests.Should().BeEmpty();

        _movie.PremiereDate = new DateTime(2026, 7, 7);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "fixing the date clears the review");
    }

    [Fact]
    public async Task Process_ShouldRetryLater_WhenPhishInDoesNotHaveTheShowYet()
    {
        _audio.Status = PhishInShowStatus.NotFound;

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Pending);
        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Pending);
        state.Attempts.Should().Be(1);
        state.FirstPendingUtc.Should().Be(_time.Now);
        state.NextAttemptUtc.Should().Be(_time.Now + TimeSpan.FromHours(1));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged, "the retry isn't due");
        _audio.ShowRequests.Should().HaveCount(1);

        _time.Advance(TimeSpan.FromHours(1));
        await Process();
        (await Record())!.Attempts.Should().Be(2);

        _audio.Status = PhishInShowStatus.Ok;
        _time.Advance(TimeSpan.FromHours(6));
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Process_ShouldGiveUp_AfterSixtyDaysWithoutAudio()
    {
        _audio.Status = PhishInShowStatus.NoAudio;
        await Process();
        _time.Advance(TimeSpan.FromDays(61));

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Skipped);
        (await Record())!.Status.Should().Be(ChapterStatus.Skipped);
    }

    [Fact]
    public async Task Process_ShouldSendToReview_AShowWhoseTrackListCannotBuildATimeline()
    {
        _audio.Status = PhishInShowStatus.Invalid;

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.NeedsReview);
        _decoder.FileDecodes.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldSkipWithoutFetching_AVideoTooShortToBeAShow()
    {
        _movie.RunTimeTicks = TimeSpan.FromMinutes(5).Ticks;

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Skipped);
        outcome.Reason.Should().StartWith("too-short");
        _audio.ShowRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_ShouldSkipWithoutReading_AClipWhoseRuntimeIsFarFromTheShow()
    {
        _movie.RunTimeTicks = TimeSpan.FromMinutes(20).Ticks;

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Skipped);
        outcome.Reason.Should().StartWith("duration-mismatch");
        _decoder.FileDecodes.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldSendToReview_WhenTheAlignmentFindsNoAnchors()
    {
        var rng = new Random(3);
        _audio.Templates = Synthetic.Tracks.Select(_ => NccTests.RandomEnvelope(rng, AlignmentConstants.TemplateFrames)).ToList();

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.NeedsReview);
        outcome.Reason.Should().StartWith("few-anchors");
        _db.Saves.Should().Be(0);
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged, "review waits for the admin or a new algorithm");
    }

    [Fact]
    public async Task Process_ShouldRealignAReviewItem_WhenTheAlgorithmVersionChanges()
    {
        await _service.Store.WriteAsync(_movie.Id, new ChapterState { Status = ChapterStatus.NeedsReview, AlgorithmVersion = 0, Fingerprint = FileFingerprint.Of(_movie.Path) }, CancellationToken.None);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Process_ShouldLeaveAnIgnoredItemAlone_UntilItsFileChanges()
    {
        await _service.Store.WriteAsync(_movie.Id, new ChapterState { Status = ChapterStatus.Ignored, AlgorithmVersion = 0, Fingerprint = FileFingerprint.Of(_movie.Path) }, CancellationToken.None);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);

        File.WriteAllBytes(_movie.Path, new byte[2000]);
        File.SetLastWriteTimeUtc(_movie.Path, _time.Now - TimeSpan.FromHours(1));
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Process_ShouldRealign_WhenTheFileIsReplaced()
    {
        await Process();
        File.WriteAllBytes(_movie.Path, new byte[2000]);
        File.SetLastWriteTimeUtc(_movie.Path, _time.Now - TimeSpan.FromHours(1));
        _db.Chapters.Clear();

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
        _decoder.FileDecodes.Should().Be(2);
    }

    [Fact]
    public async Task Process_ShouldWaitWithoutReading_AFileWrittenInTheLastTenMinutes()
    {
        File.SetLastWriteTimeUtc(_movie.Path, _time.Now - TimeSpan.FromMinutes(2));

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Pending);
        outcome.Reason.Should().Be(ChapterMarkerService.FileChangingReason);
        _decoder.FileDecodes.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldDiscardTheResult_WhenTheFileChangesWhileItIsRead()
    {
        _decoder.During = () => File.AppendAllText(_movie.Path, "more");

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.Pending);
        outcome.Reason.Should().Be(ChapterMarkerService.FileChangingReason);
        _db.Saves.Should().Be(0);
        var state = await Record();
        state!.Attempts.Should().Be(0, "a changing file isn't a failed check");
        state.Fingerprint.Should().Be(FileFingerprint.Of(_movie.Path));
    }

    [Fact]
    public async Task Process_ShouldWriteNoRecord_WhenCancelledMidShow()
    {
        using var cts = new CancellationTokenSource();
        _decoder.During = cts.Cancel;

        var act = () => _service.ProcessAsync(_movie, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await Record()).Should().BeNull();
        _db.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldRunTheSameItemOnce_WhenAnEventAndTheTaskOverlap()
    {
        var release = new TaskCompletionSource();
        _decoder.Gate = release.Task;

        var first = _service.ProcessAsync(_movie, null, CancellationToken.None);
        await _decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await _service.ProcessAsync(_movie, null, CancellationToken.None);
        release.SetResult();

        second.Kind.Should().Be(ChapterOutcomeKind.AlreadyRunning);
        (await first).Kind.Should().Be(ChapterOutcomeKind.Applied);
        _decoder.FileDecodes.Should().Be(1);
    }

    [Fact]
    public async Task Process_ShouldStopAndWait_WhenPhishInRateLimits()
    {
        _audio.TemplateError = new PhishInRateLimitedException(HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(30));

        var outcome = await Process();

        outcome.Kind.Should().Be(ChapterOutcomeKind.RateLimited);
        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Pending);
        state.Attempts.Should().Be(0);
        state.NextAttemptUtc.Should().Be(_time.Now + TimeSpan.FromMinutes(30));
        outcome.RetryAtUtc.Should().Be(_time.Now + TimeSpan.FromMinutes(30));
        _decoder.FileDecodes.Should().Be(0, "templates come first, so a rate limit costs no video read");

        var other = new Movie { Id = Guid.NewGuid(), Name = _movie.Name, Path = _movie.Path, RunTimeTicks = _movie.RunTimeTicks };
        _audio.TemplateError = null;
        (await _service.ProcessAsync(other, null, CancellationToken.None)).Kind.Should().Be(ChapterOutcomeKind.RateLimited, "the pause holds for every show");
        _audio.ShowRequests.Should().HaveCount(1);

        _time.Advance(TimeSpan.FromMinutes(30));
        (await _service.ProcessAsync(other, null, CancellationToken.None)).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Process_ShouldNotReadTheVideo_WhenATrackFails()
    {
        _audio.TemplateError = new HttpRequestException("track 404");

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Failed);

        _decoder.FileDecodes.Should().Be(0);
    }

    [Fact]
    public async Task Process_ShouldKeepTheSixtyDayClock_WhenAnErrorInterruptsTheWait()
    {
        _audio.Status = PhishInShowStatus.NotFound;
        await Process();
        var first = (await Record())!.FirstPendingUtc;
        _time.Advance(TimeSpan.FromHours(1));
        _audio.Status = PhishInShowStatus.Ok;
        _audio.TemplateError = new HttpRequestException("blip");
        await Process();
        _time.Advance(TimeSpan.FromHours(1));
        _audio.Status = PhishInShowStatus.NotFound;

        await Process();

        (await Record())!.FirstPendingUtc.Should().Be(first);
    }

    [Fact]
    public async Task Process_ShouldKeepAnAppliedShow_WhenItsPremiereDateChangesLater()
    {
        await Process();
        _movie.PremiereDate = new DateTime(2026, 7, 8);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged);
        (await Record())!.Status.Should().Be(ChapterStatus.Applied);
    }

    [Fact]
    public async Task Process_ShouldRealign_WhenTheCachedChaptersNoLongerFitTheRuntime()
    {
        await _service.Store.WriteAsync(
            _movie.Id,
            new ChapterState { Status = ChapterStatus.Applied, AlgorithmVersion = 1, Fingerprint = FileFingerprint.Of(_movie.Path), Chapters = [new AlignedChapter("Ghost", 1e6, 0.9)] },
            CancellationToken.None);

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
        _db.Chapters.Should().HaveCount(12);
    }

    [Fact]
    public async Task Process_ShouldReplaceOurOwnOldChapters_WhenTheFileIsSwappedBeforeAReprobe()
    {
        await Process();
        File.WriteAllBytes(_movie.Path, new byte[2000]);
        File.SetLastWriteTimeUtc(_movie.Path, _time.Now - TimeSpan.FromHours(1));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "the chapters still in Jellyfin are the ones we saved");
        _decoder.FileDecodes.Should().Be(2);
    }

    [Fact]
    public async Task Process_ShouldAlign_AFileDatedInTheFuture()
    {
        File.SetLastWriteTimeUtc(_movie.Path, _time.Now + TimeSpan.FromDays(2));

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied);
    }

    [Fact]
    public async Task Process_ShouldBackOffOnErrors_AndSendToReviewAfterFive()
    {
        _audio.TemplateError = new HttpRequestException("boom");

        for (int i = 1; i <= RetrySchedule.MaxFailedAttempts; i++)
        {
            (await Process()).Kind.Should().Be(ChapterOutcomeKind.Failed);
            var state = await Record();
            state!.Attempts.Should().Be(i);
            if (i < RetrySchedule.MaxFailedAttempts)
            {
                state.Status.Should().Be(ChapterStatus.Failed);
                (await Process()).Kind.Should().Be(ChapterOutcomeKind.Unchanged, "the retry isn't due");
                _time.Advance(state.NextAttemptUtc!.Value - _time.Now);
            }
            else
            {
                state.Status.Should().Be(ChapterStatus.NeedsReview);
                state.Reason.Should().Contain("boom");
            }
        }
    }

    [Fact]
    public async Task Process_ShouldKeepTheAppliedRecord_WhenAReapplyFails()
    {
        await Process();
        _db.Chapters.Clear();
        _db.SaveError = new IOException("db locked");

        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Failed);

        var state = await Record();
        state!.Status.Should().Be(ChapterStatus.Applied);
        state.Chapters.Should().HaveCount(12);
    }

    [Fact]
    public async Task Process_ShouldLoseNothing_WhenTheRecordIsLostAfterTheSave()
    {
        await Process();
        var saved = _db.Chapters.ToList();
        File.Delete(Path.Combine(_dir, "state", _movie.Id.ToString("N") + ".json"));

        // Rare now (the record is written even if the caller cancels), but without it our chapters look like anyone's.
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Skipped, "without a record our chapters look like anyone's");
        _db.Chapters.Should().BeEquivalentTo(saved);

        _db.Chapters.Clear();
        (await Process()).Kind.Should().Be(ChapterOutcomeKind.Applied, "once they're gone the show is aligned again");
    }

    [Fact]
    public async Task Process_ShouldRespectTheRunBudget()
    {
        var budget = new RunBudget(maxShows: 1, maxBytes: 10_000);

        (await _service.ProcessAsync(_movie, budget, CancellationToken.None)).Kind.Should().Be(ChapterOutcomeKind.Applied);
        budget.BytesRead.Should().Be(1000);
        budget.Exhausted.Should().BeTrue();

        var other = new Movie { Id = Guid.NewGuid(), Name = _movie.Name, Path = _movie.Path, RunTimeTicks = _movie.RunTimeTicks };
        _db.Chapters.Clear(); // the fake table isn't per item
        (await _service.ProcessAsync(other, budget, CancellationToken.None)).Kind.Should().Be(ChapterOutcomeKind.OverBudget);
        _decoder.FileDecodes.Should().Be(1);
        (await _service.Store.ReadAsync(other.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void RunBudget_ShouldLetTheFirstVideoThrough_EvenWhenItIsLargerThanTheBudget()
    {
        var budget = new RunBudget(maxShows: 5, maxBytes: 100);

        budget.CanRead(500).Should().BeTrue();
        budget.Charge(500);
        budget.CanRead(1).Should().BeFalse();
    }

    private Task<ChapterOutcome> Process() => _service.ProcessAsync(_movie, null, CancellationToken.None);

    private Task<ChapterState?> Record() => _service.Store.ReadAsync(_movie.Id, CancellationToken.None);

    private List<ChapterInfo> DummySet()
    {
        long step = TimeSpan.FromSeconds(300).Ticks;
        int count = (int)(_movie.RunTimeTicks!.Value / step);
        return Enumerable.Range(0, count)
            .Select(i => new ChapterInfo { StartPositionTicks = i * step, Name = string.Format(CultureInfo.InvariantCulture, "Chapter {0}", i + 1) })
            .ToList();
    }

    private sealed class FakeChapters
    {
        public FakeChapters()
        {
            var mock = new Mock<IChapterManager>();
            mock.Setup(m => m.GetChapters(It.IsAny<Guid>())).Returns(() => Chapters.ToList());
            mock.Setup(m => m.SaveChapters(It.IsAny<BaseItem>(), It.IsAny<IReadOnlyList<ChapterInfo>>()))
                .Callback<BaseItem, IReadOnlyList<ChapterInfo>>((_, list) =>
                {
                    if (SaveError is not null)
                    {
                        throw SaveError;
                    }

                    Chapters = list.ToList();
                    Saves++;
                });
            Object = mock.Object;
        }

        public IChapterManager Object { get; }

        public List<ChapterInfo> Chapters { get; set; } = new();

        public int Saves { get; private set; }

        public Exception? SaveError { get; set; }
    }

    private sealed class FakeAudio : IShowAudioSource
    {
        public PhishInShowStatus Status { get; set; } = PhishInShowStatus.Ok;

        public List<float[]?>? Templates { get; set; }

        public Exception? TemplateError { get; set; }

        public List<DateTime> ShowRequests { get; } = new();

        public Task<PhishInShow> GetShowAsync(DateTime showDate, CancellationToken cancellationToken)
        {
            ShowRequests.Add(showDate);
            if (Status != PhishInShowStatus.Ok)
            {
                return Task.FromResult(new PhishInShow(Status, Status.ToString(), Array.Empty<PhishInTrack>()));
            }

            var tracks = Synthetic.Tracks.Select((t, i) => new PhishInTrack(t.Title, i + 1, "Set 1", t.DurationSeconds, $"https://phish.in/blob/{i}.mp3")).ToList();
            return Task.FromResult(new PhishInShow(PhishInShowStatus.Ok, null, tracks));
        }

        public Task<ShowTemplates> GetTemplatesAsync(PhishInShow show, CancellationToken cancellationToken)
        {
            if (TemplateError is not null)
            {
                throw TemplateError;
            }

            var templates = Templates ?? Synthetic.Tracks.Select(t => t.Template).ToList();
            return Task.FromResult(new ShowTemplates(templates, 0, templates.Count, 0, 0, 0));
        }
    }

    private sealed class FakeDecoder : IEnvelopeDecoder
    {
        public int FileDecodes { get; private set; }

        public Action? During { get; set; }

        public Task? Gate { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<float[]> DecodeFileAsync(string path, CancellationToken cancellationToken)
        {
            FileDecodes++;
            Started.TrySetResult();
            if (Gate is not null)
            {
                await Gate;
            }

            During?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Synthetic.Video;
        }

        public Task<TemplateDecodeResult> DecodeTemplateAsync(Stream source, long maxBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
