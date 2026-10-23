using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Jellyfin.Plugin.PhishNet.Configuration;
using Jellyfin.Plugin.PhishNet.Parsers;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>What one <see cref="ChapterMarkerService.ProcessAsync"/> call did.</summary>
public enum ChapterOutcomeKind
{
    /// <summary>Not a Phish show with a date, not probed yet, or the file is gone. No record is kept.</summary>
    NotEligible,

    /// <summary>Another caller is processing the same item.</summary>
    AlreadyRunning,

    /// <summary>Nothing to do: already applied, waiting for a retry, or settled.</summary>
    Unchanged,

    /// <summary>The run's budget can't cover reading this video; try in a later run.</summary>
    OverBudget,

    /// <summary>Aligned and saved.</summary>
    Applied,

    /// <summary>Cached chapters saved again after Jellyfin dropped them.</summary>
    Reapplied,

    /// <summary>Other real chapters replaced ours; left alone.</summary>
    Superseded,

    /// <summary>Not a show to chapter.</summary>
    Skipped,

    /// <summary>Retried later.</summary>
    Pending,

    /// <summary>Waits for the admin.</summary>
    NeedsReview,

    /// <summary>An error; retried with backoff.</summary>
    Failed,

    /// <summary>phish.in asked us to slow down; the caller should stop for now.</summary>
    RateLimited,
}

/// <summary>
/// What one <see cref="ChapterMarkerService.ProcessAsync"/> call did.
/// </summary>
/// <param name="Kind">The outcome.</param>
/// <param name="Reason">Why, when there is more to say.</param>
public sealed record ChapterOutcome(ChapterOutcomeKind Kind, string? Reason = null);

/// <summary>
/// The limits of one scheduled run: how many shows to align and how much video to read. Used by one
/// caller at a time.
/// </summary>
public sealed class RunBudget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunBudget"/> class.
    /// </summary>
    /// <param name="maxShows">Most shows to align.</param>
    /// <param name="maxBytes">Most video bytes to read.</param>
    public RunBudget(int maxShows, long maxBytes)
    {
        ShowsLeft = maxShows;
        BytesLeft = maxBytes;
    }

    /// <summary>Gets how many more shows may be aligned.</summary>
    public int ShowsLeft { get; private set; }

    /// <summary>Gets how many more video bytes may be read.</summary>
    public long BytesLeft { get; private set; }

    /// <summary>Gets the video bytes read so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Gets a value indicating whether nothing more can be aligned this run.</summary>
    public bool Exhausted => ShowsLeft <= 0 || BytesLeft <= 0;

    /// <summary>
    /// Says whether a video of this size may be read. The first video of a run always may, so one file
    /// larger than the whole budget still gets its turn.
    /// </summary>
    /// <param name="bytes">The video's size.</param>
    /// <returns>True when it fits.</returns>
    public bool CanRead(long bytes) => !Exhausted && (bytes <= BytesLeft || BytesRead == 0);

    /// <summary>
    /// Charges a video read against the budget.
    /// </summary>
    /// <param name="bytes">The video's size.</param>
    public void Charge(long bytes)
    {
        ShowsLeft--;
        BytesLeft -= bytes;
        BytesRead += bytes;
    }
}

/// <summary>
/// Adds chapter markers to one Phish show at a time (plan §4.2–§4.6). Library events and the scheduled task
/// both call <see cref="ProcessAsync"/>; a global lock keeps it to one show at a time and an in-flight set
/// keeps the same item from running twice. Chapters go to Jellyfin's database first, then the item's
/// record is written, so a crash in between loses nothing.
/// </summary>
public sealed class ChapterMarkerService
{
    /// <summary>The alignment version written into records. Bump it to re-align Skipped and NeedsReview items.</summary>
    public const int AlgorithmVersion = 1;

    /// <summary>Reason for an item whose database already holds real chapters.</summary>
    public const string HasChaptersReason = "has-chapters";

    /// <summary>Reason for an item whose file changed while it was being read.</summary>
    public const string FileChangingReason = "file-changing";

    /// <summary>Start of the reason for an item whose premiere date disagrees with its file name.</summary>
    public const string DateMismatchReason = "date-mismatch";

    /// <summary>A file written more recently than this may still be copying, so it waits.</summary>
    public static readonly TimeSpan MinFileAge = TimeSpan.FromMinutes(10);

    /// <summary>The wait after a rate limit that didn't say how long.</summary>
    public static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromHours(1);

    private static readonly PhishFileNameParser Parser = new(NullLogger<PhishFileNameParser>.Instance);

    private readonly IChapterManager _chapters;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILocalizationManager _localization;
    private readonly IShowAudioSource _audio;
    private readonly IEnvelopeDecoder _decoder;
    private readonly ChapterStateStore _store;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _time;
    private readonly ILogger<ChapterMarkerService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterMarkerService"/> class.
    /// </summary>
    /// <param name="chapters">Jellyfin's chapter store.</param>
    /// <param name="serverConfig">Server configuration, for the dummy chapter setting.</param>
    /// <param name="localization">Localization, for the dummy chapter names.</param>
    /// <param name="audio">Show track lists and templates.</param>
    /// <param name="decoder">The video decoder.</param>
    /// <param name="store">Per-item records.</param>
    /// <param name="config">Reads the current plugin configuration.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public ChapterMarkerService(
        IChapterManager chapters,
        IServerConfigurationManager serverConfig,
        ILocalizationManager localization,
        IShowAudioSource audio,
        IEnvelopeDecoder decoder,
        ChapterStateStore store,
        Func<PluginConfiguration> config,
        TimeProvider time,
        ILogger<ChapterMarkerService> logger)
    {
        _chapters = chapters;
        _serverConfig = serverConfig;
        _localization = localization;
        _audio = audio;
        _decoder = decoder;
        _store = store;
        _config = config;
        _time = time;
        _logger = logger;
    }

    /// <summary>Gets the per-item records.</summary>
    public ChapterStateStore Store => _store;

    /// <summary>
    /// Reads the show date from an item's file name (or, failing that, its title), with or without
    /// Phish.net data.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The show date, or null when the item isn't a dated Phish show.</returns>
    public static DateTime? ShowDateOf(BaseItem item)
    {
        var path = item.Path;
        if (string.IsNullOrEmpty(path) || !PhishFileNameParser.LooksPhishRelated(item.Name, path))
        {
            return null;
        }

        return Parser.Parse(Path.GetFileName(path), Path.GetDirectoryName(path)).ShowDate
            ?? (string.IsNullOrEmpty(item.Name) ? null : Parser.Parse(item.Name).ShowDate);
    }

    /// <summary>
    /// Adds, restores or leaves alone one item's chapters.
    /// </summary>
    /// <param name="item">The video.</param>
    /// <param name="budget">The scheduled run's budget, or null for a library event.</param>
    /// <param name="cancellationToken">Cancellation token; cancelling writes no record.</param>
    /// <returns>What happened.</returns>
    public async Task<ChapterOutcome> ProcessAsync(Video item, RunBudget? budget, CancellationToken cancellationToken)
    {
        if (!_inFlight.TryAdd(item.Id, 0))
        {
            return new ChapterOutcome(ChapterOutcomeKind.AlreadyRunning);
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ProcessLockedAsync(item, budget, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            _inFlight.TryRemove(item.Id, out _);
        }
    }

    private static bool Changed(Job job) => FileFingerprint.Of(job.Path) != job.Fingerprint;

    private async Task<ChapterOutcome> ProcessLockedAsync(Video item, RunBudget? budget, CancellationToken cancellationToken)
    {
        long runtime = item.RunTimeTicks ?? 0;
        if (runtime <= 0 || ShowDateOf(item) is not { } showDate)
        {
            return new ChapterOutcome(ChapterOutcomeKind.NotEligible);
        }

        if (FileFingerprint.Of(item.Path) is not { } fingerprint)
        {
            return new ChapterOutcome(ChapterOutcomeKind.NotEligible, "file-missing");
        }

        var state = await _store.ReadAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (state is not null && state.Fingerprint != fingerprint)
        {
            state = null; // a new file, so an old result (even Ignored) no longer applies
        }

        var job = new Job(item, item.Path, showDate.Date, fingerprint, runtime, state, _time.GetUtcNow().UtcDateTime);
        try
        {
            return await DecideAsync(job, budget, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PhishInRateLimitedException ex)
        {
            var wait = ex.RetryAfter is { } after && after > TimeSpan.Zero ? after : DefaultRateLimitWait;
            await RecordAsync(job, PendingCarry(job, ChapterStatus.Pending, $"rate-limited ({(int)ex.StatusCode})", job.Now + wait), cancellationToken).ConfigureAwait(false);
            return new ChapterOutcome(ChapterOutcomeKind.RateLimited, ex.Message);
        }
        catch (Exception ex)
        {
            return await FailAsync(job, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChapterOutcome> DecideAsync(Job job, RunBudget? budget, CancellationToken cancellationToken)
    {
        var state = job.Previous;
        if (state is { Status: ChapterStatus.Ignored })
        {
            return new ChapterOutcome(ChapterOutcomeKind.Unchanged, "ignored");
        }

        if (job.Item.PremiereDate is { } premiere && premiere.Date != job.ShowDate)
        {
            var mismatch = string.Format(CultureInfo.InvariantCulture, "{0} premiere={1:yyyy-MM-dd} file={2:yyyy-MM-dd}", DateMismatchReason, premiere, job.ShowDate);
            return state is { Status: ChapterStatus.NeedsReview } && state.Reason == mismatch
                ? new ChapterOutcome(ChapterOutcomeKind.Unchanged, mismatch)
                : await SettleAsync(job, ChapterStatus.NeedsReview, mismatch, null, cancellationToken).ConfigureAwait(false);
        }

        if (state is { Status: ChapterStatus.NeedsReview } && state.Reason?.StartsWith(DateMismatchReason, StringComparison.Ordinal) == true)
        {
            job.Previous = state = null; // the dates agree now
        }

        var situation = Classify(job);
        if (state is { Status: ChapterStatus.Applied or ChapterStatus.Superseded })
        {
            return await KeepAppliedAsync(job, situation, cancellationToken).ConfigureAwait(false);
        }

        if (situation == ChapterSituation.Other)
        {
            return state is { Status: ChapterStatus.Skipped, Reason: HasChaptersReason }
                ? new ChapterOutcome(ChapterOutcomeKind.Unchanged, HasChaptersReason)
                : await SettleAsync(job, ChapterStatus.Skipped, HasChaptersReason, null, cancellationToken).ConfigureAwait(false);
        }

        if (state is { Status: ChapterStatus.Skipped, Reason: HasChaptersReason })
        {
            job.Previous = state = null; // those chapters are gone
        }

        if (state is { Status: ChapterStatus.Pending or ChapterStatus.Failed, NextAttemptUtc: { } next } && next > job.Now)
        {
            return new ChapterOutcome(ChapterOutcomeKind.Unchanged, $"waiting until {next:u}");
        }

        if (state is { Status: ChapterStatus.Skipped or ChapterStatus.NeedsReview } && state.AlgorithmVersion == AlgorithmVersion)
        {
            return new ChapterOutcome(ChapterOutcomeKind.Unchanged, state.Reason);
        }

        return await AlignAsync(job, budget, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChapterOutcome> KeepAppliedAsync(Job job, ChapterSituation situation, CancellationToken cancellationToken)
    {
        var state = job.Previous!;
        switch (situation)
        {
            case ChapterSituation.Ours:
                if (state.Status == ChapterStatus.Superseded)
                {
                    await RecordAsync(job, Copy(state, ChapterStatus.Applied, null, job.Now), cancellationToken).ConfigureAwait(false);
                }

                return new ChapterOutcome(ChapterOutcomeKind.Unchanged, "applied");
            case ChapterSituation.Other:
                if (state.Status == ChapterStatus.Superseded)
                {
                    return new ChapterOutcome(ChapterOutcomeKind.Unchanged, "superseded");
                }

                await RecordAsync(job, Copy(state, ChapterStatus.Superseded, "other chapters replaced ours", job.Now), cancellationToken).ConfigureAwait(false);
                Log(job, "SUPERSEDED", "other chapters replaced ours; left alone");
                return new ChapterOutcome(ChapterOutcomeKind.Superseded);
            default:
                if (ChapterValidator.Validate(state.Chapters, job.Runtime) is { } invalid)
                {
                    return await SettleAsync(job, ChapterStatus.NeedsReview, "cached chapters no longer fit: " + invalid, null, cancellationToken).ConfigureAwait(false);
                }

                _chapters.SaveChapters(job.Item, ChapterDetector.ToChapterInfos(state.Chapters));
                await RecordAsync(job, Copy(state, ChapterStatus.Applied, null, job.Now), cancellationToken).ConfigureAwait(false);
                Log(job, "REAPPLY", $"{state.Chapters.Count} ch restored after Jellyfin dropped them");
                return new ChapterOutcome(ChapterOutcomeKind.Reapplied);
        }
    }

    private async Task<ChapterOutcome> AlignAsync(Job job, RunBudget? budget, CancellationToken cancellationToken)
    {
        if (job.Now - job.Fingerprint.ModifiedUtc < MinFileAge)
        {
            return await FileChangingAsync(job, cancellationToken).ConfigureAwait(false);
        }

        double runtimeSeconds = job.Runtime / (double)TimeSpan.TicksPerSecond;
        if (runtimeSeconds < AlignmentConstants.MinVideoSeconds)
        {
            return await SettleAsync(job, ChapterStatus.Skipped, AlignmentGates.CheckVideoLength(runtimeSeconds), null, cancellationToken).ConfigureAwait(false);
        }

        var fetch = Stopwatch.StartNew();
        var show = await _audio.GetShowAsync(job.ShowDate, cancellationToken).ConfigureAwait(false);
        switch (show.Status)
        {
            case PhishInShowStatus.NotFound:
            case PhishInShowStatus.NoAudio:
                return await NoAudioYetAsync(job, show.Detail ?? "no-audio", cancellationToken).ConfigureAwait(false);
            case PhishInShowStatus.Invalid:
                return await SettleAsync(job, ChapterStatus.NeedsReview, show.Detail, null, cancellationToken).ConfigureAwait(false);
        }

        // Checked on the runtime first, so a clip isn't read end to end just to be rejected.
        if (AlignmentGates.CheckDuration(runtimeSeconds, show.TotalSeconds) is { } clip)
        {
            return await SettleAsync(job, ChapterStatus.Skipped, clip, null, cancellationToken).ConfigureAwait(false);
        }

        if (budget is not null && !budget.CanRead(job.Fingerprint.Size))
        {
            return new ChapterOutcome(ChapterOutcomeKind.OverBudget);
        }

        fetch.Stop();
        var read = Stopwatch.StartNew();
        float[] video;
        try
        {
            video = await _decoder.DecodeFileAsync(job.Path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            budget?.Charge(job.Fingerprint.Size);
        }

        read.Stop();
        double videoSeconds = video.Length / (double)AlignmentConstants.Fps;
        if ((AlignmentGates.CheckVideoLength(videoSeconds) ?? AlignmentGates.CheckDuration(videoSeconds, show.TotalSeconds)) is { } gate)
        {
            return await SettleAsync(job, ChapterStatus.Skipped, gate, null, cancellationToken).ConfigureAwait(false);
        }

        if (Changed(job))
        {
            return await FileChangingAsync(job, cancellationToken).ConfigureAwait(false);
        }

        fetch.Start();
        var templates = await _audio.GetTemplatesAsync(show, cancellationToken).ConfigureAwait(false);
        fetch.Stop();

        var align = Stopwatch.StartNew();
        var tracks = show.Tracks.Select((t, i) => new AlignmentTrack(t.Title, t.DurationSeconds, templates.Templates[i])).ToList();
        var result = ConsensusAligner.Align(video, tracks);
        align.Stop();
        if (result.Chapters is not { } rows)
        {
            return await SettleAsync(job, ChapterStatus.NeedsReview, result.SkipReason, result, cancellationToken).ConfigureAwait(false);
        }

        LogTable(job, rows);
        var rejected = AlignmentGates.CheckMedian(result.MedianConfidence) ?? ChapterValidator.Validate(rows, job.Runtime);
        if (rejected is not null)
        {
            return await SettleAsync(job, ChapterStatus.NeedsReview, rejected, result, cancellationToken).ConfigureAwait(false);
        }

        if (Changed(job))
        {
            return await FileChangingAsync(job, cancellationToken).ConfigureAwait(false);
        }

        if (Classify(job) == ChapterSituation.Other)
        {
            // Real chapters arrived while we were aligning (a probe, a person); they win.
            return await SettleAsync(job, ChapterStatus.Skipped, HasChaptersReason, null, cancellationToken).ConfigureAwait(false);
        }

        _chapters.SaveChapters(job.Item, ChapterDetector.ToChapterInfos(rows));
        await RecordAsync(job, New(job, ChapterStatus.Applied, null, result), cancellationToken).ConfigureAwait(false);
        Log(job, "DONE", string.Format(
            CultureInfo.InvariantCulture,
            "{0} ch, median {1:0.00}, {2} low (read {3:0}s, fetch {4:0}s, align {5:0.0}s)",
            rows.Count,
            result.MedianConfidence,
            result.LowConfidenceCount,
            read.Elapsed.TotalSeconds,
            fetch.Elapsed.TotalSeconds,
            align.Elapsed.TotalSeconds));
        return new ChapterOutcome(ChapterOutcomeKind.Applied);
    }

    private ChapterSituation Classify(Job job)
    {
        var ours = job.Previous is { Status: ChapterStatus.Applied or ChapterStatus.Superseded } p ? p.Chapters : null;
        return ChapterDetector.Classify(_chapters.GetChapters(job.Item.Id), ours, DummiesFor(job.Runtime));
    }

    private DummySignature? DummiesFor(long runtime)
    {
        int duration = _serverConfig.Configuration.DummyChapterDuration;
        if (!_config().ReplaceDummyChapters || duration <= 0)
        {
            return null;
        }

        return new DummySignature(runtime, duration, _localization.GetLocalizedString("ChapterNameValue"));
    }

    private async Task<ChapterOutcome> NoAudioYetAsync(Job job, string detail, CancellationToken cancellationToken)
    {
        var previous = job.Previous is { Status: ChapterStatus.Pending } p ? p : null;
        int attempts = (previous?.Attempts ?? 0) + 1;
        var first = previous?.FirstPendingUtc ?? job.Now;
        if (RetrySchedule.NextPendingAttempt(first, attempts, job.Now) is not { } next)
        {
            return await SettleAsync(job, ChapterStatus.Skipped, $"{detail}; gave up after {attempts} checks", null, cancellationToken).ConfigureAwait(false);
        }

        var state = New(job, ChapterStatus.Pending, detail, null);
        state.Attempts = attempts;
        state.FirstPendingUtc = first;
        state.NextAttemptUtc = next;
        await RecordAsync(job, state, cancellationToken).ConfigureAwait(false);
        Log(job, "PENDING", $"{detail}; check {attempts}, next {next:u}");
        return new ChapterOutcome(ChapterOutcomeKind.Pending, detail);
    }

    private async Task<ChapterOutcome> FileChangingAsync(Job job, CancellationToken cancellationToken)
    {
        // The latest stat goes in the record, so a file that settles keeps its retry time.
        var now = FileFingerprint.Of(job.Path);
        if (now is null)
        {
            return new ChapterOutcome(ChapterOutcomeKind.NotEligible, "file-missing");
        }

        job.Fingerprint = now;
        await RecordAsync(job, PendingCarry(job, ChapterStatus.Pending, FileChangingReason, job.Now + MinFileAge), cancellationToken).ConfigureAwait(false);
        Log(job, "PENDING", FileChangingReason);
        return new ChapterOutcome(ChapterOutcomeKind.Pending, FileChangingReason);
    }

    private async Task<ChapterOutcome> FailAsync(Job job, Exception ex, CancellationToken cancellationToken)
    {
        if (job.Previous is { Status: ChapterStatus.Applied or ChapterStatus.Superseded })
        {
            // Keep the good result; the next pass tries the re-apply again.
            _logger.LogError(ex, "Chapters FAILED {Date} \"{File}\"; record left as {Status}", Date(job), FileName(job), job.Previous.Status);
            return new ChapterOutcome(ChapterOutcomeKind.Failed, ex.Message);
        }

        int attempts = (job.Previous is { Status: ChapterStatus.Failed } p ? p.Attempts : 0) + 1;
        var next = RetrySchedule.NextFailedAttempt(attempts, job.Now);
        var reason = $"{ex.GetType().Name}: {ex.Message}";
        var state = next is null
            ? New(job, ChapterStatus.NeedsReview, $"failed {attempts} times; last: {reason}", null)
            : New(job, ChapterStatus.Failed, reason, null);
        state.Attempts = attempts;
        state.NextAttemptUtc = next;
        _logger.LogError(ex, "Chapters FAILED {Date} \"{File}\": attempt {Attempts}, next {Next}", Date(job), FileName(job), attempts, next?.ToString("u", CultureInfo.InvariantCulture) ?? "review");
        try
        {
            await RecordAsync(job, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception writeError) when (writeError is not OperationCanceledException)
        {
            _logger.LogError(writeError, "Chapters: couldn't write the record for {File}", FileName(job));
        }

        return new ChapterOutcome(ChapterOutcomeKind.Failed, reason);
    }

    /// <summary>Writes a Skipped or NeedsReview record and logs it.</summary>
    private async Task<ChapterOutcome> SettleAsync(Job job, ChapterStatus status, string? reason, AlignmentResult? result, CancellationToken cancellationToken)
    {
        await RecordAsync(job, New(job, status, reason, result), cancellationToken).ConfigureAwait(false);
        Log(job, status == ChapterStatus.Skipped ? "SKIP" : "REVIEW", reason ?? string.Empty);
        return new ChapterOutcome(status == ChapterStatus.Skipped ? ChapterOutcomeKind.Skipped : ChapterOutcomeKind.NeedsReview, reason);
    }

    private Task RecordAsync(Job job, ChapterState state, CancellationToken cancellationToken) =>
        _store.WriteAsync(job.Item.Id, state, cancellationToken);

    private static ChapterState New(Job job, ChapterStatus status, string? reason, AlignmentResult? result) => new()
    {
        AlgorithmVersion = AlgorithmVersion,
        Status = status,
        Reason = reason,
        Fingerprint = job.Fingerprint,
        ShowDate = job.ShowDate,
        Chapters = result?.Chapters?.ToList() ?? new List<AlignedChapter>(),
        MedianConfidence = result?.Chapters is null ? null : result.MedianConfidence,
        LowConfidenceCount = result?.LowConfidenceCount ?? 0,
        UpdatedUtc = job.Now,
    };

    /// <summary>A Pending record that doesn't count as a check: the attempts and first-pending time carry over.</summary>
    private static ChapterState PendingCarry(Job job, ChapterStatus status, string reason, DateTime next)
    {
        var state = New(job, status, reason, null);
        if (job.Previous is { Status: ChapterStatus.Pending } p)
        {
            state.Attempts = p.Attempts;
            state.FirstPendingUtc = p.FirstPendingUtc;
        }

        state.NextAttemptUtc = next;
        return state;
    }

    private static ChapterState Copy(ChapterState state, ChapterStatus status, string? reason, DateTime now) => new()
    {
        AlgorithmVersion = state.AlgorithmVersion,
        Status = status,
        Reason = reason,
        Fingerprint = state.Fingerprint,
        ShowDate = state.ShowDate,
        Chapters = state.Chapters,
        MedianConfidence = state.MedianConfidence,
        LowConfidenceCount = state.LowConfidenceCount,
        UpdatedUtc = now,
    };

    private static string Date(Job job) => job.ShowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FileName(Job job) => Path.GetFileName(job.Path);

    private void Log(Job job, string verb, string detail) =>
        _logger.LogInformation("Chapters {Verb} {Date} \"{File}\": {Detail}", verb, Date(job), FileName(job), detail);

    private void LogTable(Job job, IReadOnlyList<AlignedChapter> rows)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var lines = rows.Select(r => string.Format(
            CultureInfo.InvariantCulture,
            "  {0,8}  {1,5}  {2}",
            AlignmentGates.Hms(r.StartSeconds),
            r.Confidence is { } c ? c.ToString("0.00", CultureInfo.InvariantCulture) : "fit",
            r.Title));
        _logger.LogDebug("Chapters for {Date} \"{File}\":\n{Table}", Date(job), FileName(job), string.Join('\n', lines));
    }

    private sealed class Job(Video item, string path, DateTime showDate, FileFingerprint fingerprint, long runtime, ChapterState? previous, DateTime now)
    {
        public Video Item { get; } = item;

        public string Path { get; } = path;

        public DateTime ShowDate { get; } = showDate;

        public FileFingerprint Fingerprint { get; set; } = fingerprint;

        public long Runtime { get; } = runtime;

        public ChapterState? Previous { get; set; } = previous;

        public DateTime Now { get; } = now;
    }
}
