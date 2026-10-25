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
/// <param name="RetryAtUtc">When the item is worth trying again (Pending, Failed, RateLimited), if known.</param>
public sealed record ChapterOutcome(ChapterOutcomeKind Kind, string? Reason = null, DateTime? RetryAtUtc = null);

/// <summary>How an admin action (Retry, Apply anyway, Ignore) went.</summary>
public enum ChapterActionStatus
{
    /// <summary>Done.</summary>
    Done,

    /// <summary>The item has no record (or isn't in the library).</summary>
    NotFound,

    /// <summary>The item is being processed right now; try again shortly.</summary>
    Busy,

    /// <summary>The action doesn't fit the item's state; the message says why.</summary>
    Conflict,
}

/// <summary>
/// How an admin action went.
/// </summary>
/// <param name="Status">The result.</param>
/// <param name="Message">Why, for anything but <see cref="ChapterActionStatus.Done"/>.</param>
public sealed record ChapterActionResult(ChapterActionStatus Status, string? Message = null)
{
    /// <summary>Gets the result of an action that worked.</summary>
    public static ChapterActionResult Done { get; } = new(ChapterActionStatus.Done);
}

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
/// Processes one item's chapters; what the queue and the scheduled task call.
/// </summary>
public interface IChapterMarkerService
{
    /// <summary>
    /// Adds, restores or leaves alone one item's chapters.
    /// </summary>
    /// <param name="item">The video.</param>
    /// <param name="budget">The scheduled run's budget, or null for a library event.</param>
    /// <param name="cancellationToken">Cancellation token; cancelling writes no record.</param>
    /// <returns>What happened.</returns>
    Task<ChapterOutcome> ProcessAsync(Video item, RunBudget? budget, CancellationToken cancellationToken);
}

/// <summary>
/// Adds chapter markers to one Phish show at a time (plan §4.2–§4.6). Library events and the scheduled task
/// both call <see cref="ProcessAsync"/>; a global lock keeps it to one show at a time and an in-flight set
/// keeps the same item from running twice. Chapters go to Jellyfin's database first, then the item's
/// record is written, so a crash in between loses nothing. A <c>.chapters.txt</c> sidecar next to the
/// video replaces alignment for that item. The admin actions (<see cref="RetryAsync"/>,
/// <see cref="ApplyAnywayAsync"/>, <see cref="IgnoreAsync"/>) touch one item, so they take only its
/// in-flight slot, not the global lock, and report Busy rather than wait for a show in progress.
/// </summary>
public sealed class ChapterMarkerService : IChapterMarkerService
{
    /// <summary>
    /// The alignment version written into records. Bump it to re-align Skipped and NeedsReview items.
    /// 2: per-set intercepts (<see cref="AlignmentOptions.PerSet"/>).
    /// </summary>
    public const int AlgorithmVersion = 2;

    /// <summary>Reason for an item whose database already holds real chapters.</summary>
    public const string HasChaptersReason = "has-chapters";

    /// <summary>Reason for an item whose file changed while it was being read.</summary>
    public const string FileChangingReason = "file-changing";

    /// <summary>Start of the reason for an item whose premiere date disagrees with its file name.</summary>
    public const string DateMismatchReason = "date-mismatch";

    /// <summary>Stands in for the content hash of a sidecar that couldn't be read.</summary>
    public const string UnreadableSidecarHash = "unreadable";

    /// <summary>A file written more recently than this may still be copying, so it waits.</summary>
    public static readonly TimeSpan MinFileAge = TimeSpan.FromMinutes(10);

    /// <summary>The wait after a rate limit that didn't say how long.</summary>
    public static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromHours(1);

    /// <summary>A modification time this far ahead of the clock is taken as clock skew, not a copy in progress.</summary>
    public static readonly TimeSpan FutureMtimeTolerance = TimeSpan.FromMinutes(1);

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
    private DateTime _phishInPausedUntil; // read and written under _gate

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
    /// Says whether a file may still be being written: modified less than <see cref="MinFileAge"/> ago. A
    /// time well in the future is clock skew; the stats before and after the read still catch a change.
    /// </summary>
    /// <param name="modifiedUtc">The file's last write time.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns>True when the file should be left to settle.</returns>
    public static bool StillWriting(DateTime modifiedUtc, DateTime nowUtc)
    {
        var age = nowUtc - modifiedUtc;
        return age < MinFileAge && age > -FutureMtimeTolerance;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Clears an item's waits and settled states so the next pass starts fresh: re-aligns it, or re-reads its
    /// sidecar. Chapters this plugin saved stay replaceable. The record's status is kept until that pass.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How it went.</returns>
    public Task<ChapterActionResult> RetryAsync(Guid itemId, CancellationToken cancellationToken) =>
        WithItemAsync(itemId, async () =>
        {
            if (await _store.ReadAsync(itemId, cancellationToken).ConfigureAwait(false) is not { } state)
            {
                return NoRecord;
            }

            state.RetryRequested = true;
            state.UpdatedUtc = _time.GetUtcNow().UtcDateTime;
            await _store.WriteAsync(itemId, state, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Chapters RETRY requested for {Item} ({Status})", itemId, state.Status);
            return ChapterActionResult.Done;
        });

    /// <summary>
    /// Leaves an item alone until a Retry or a change to its file or sidecar. Applied and Superseded items
    /// can't be ignored: their chapters are in Jellyfin, and ignoring would forget they're ours.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How it went.</returns>
    public Task<ChapterActionResult> IgnoreAsync(Guid itemId, CancellationToken cancellationToken) =>
        WithItemAsync(itemId, async () =>
        {
            if (await _store.ReadAsync(itemId, cancellationToken).ConfigureAwait(false) is not { } state)
            {
                return NoRecord;
            }

            switch (state.Status)
            {
                case ChapterStatus.Ignored:
                    return ChapterActionResult.Done;
                case ChapterStatus.Applied or ChapterStatus.Superseded:
                    return Conflict($"the show is {state.Status}; only shows without our chapters can be ignored");
            }

            await _store.WriteAsync(itemId, Copy(state, ChapterStatus.Ignored, state.Reason, _time.GetUtcNow().UtcDateTime), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Chapters IGNORE {Item} (was {Status}: {Reason})", itemId, state.Status, state.Reason);
            return ChapterActionResult.Done;
        });

    /// <summary>
    /// Saves a review item's aligned chapters despite the confidence gates. The structural checks still
    /// apply, the file must be the one that was aligned, and real chapters from anyone else are never
    /// replaced.
    /// </summary>
    /// <param name="item">The video.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How it went.</returns>
    public Task<ChapterActionResult> ApplyAnywayAsync(Video item, CancellationToken cancellationToken) =>
        WithItemAsync(item.Id, async () =>
        {
            if (await _store.ReadAsync(item.Id, cancellationToken).ConfigureAwait(false) is not { } state)
            {
                return NoRecord;
            }

            if (state.Status != ChapterStatus.NeedsReview || state.Chapters.Count == 0)
            {
                return Conflict("only a review item with aligned chapters can be applied anyway");
            }

            if (FileFingerprint.Of(item.Path) is not { } file || file with { SidecarHash = ReadSidecar(item.Path).Hash } != state.Fingerprint)
            {
                return Conflict("the file or its sidecar changed since it was aligned; use Retry");
            }

            long runtime = item.RunTimeTicks ?? 0;
            if (ChapterValidator.Validate(state.Chapters, runtime) is { } invalid)
            {
                return Conflict(invalid);
            }

            var situation = ChapterDetector.Classify(_chapters.GetChapters(item.Id), state.SavedChapters, DummiesFor(runtime));
            if (situation == ChapterSituation.Other)
            {
                return Conflict("the show has other chapters now, and those are never replaced");
            }

            _chapters.SaveChapters(item, ChapterDetector.ToChapterInfos(state.Chapters));
            var applied = Copy(state, ChapterStatus.Applied, null, _time.GetUtcNow().UtcDateTime);
            applied.Source = ChapterSource.ApplyAnyway;
            applied.SavedChapters = null;

            // Not cancellable once saved, as in ProcessAsync.
            await _store.WriteAsync(item.Id, applied, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation(
                "Chapters APPLIED ANYWAY {Date} {File}: {Count} ch, median {Median}",
                state.ShowDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Path.GetFileName(item.Path),
                state.Chapters.Count,
                state.MedianConfidence?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a");
            return ChapterActionResult.Done;
        });

    private static ChapterActionResult NoRecord { get; } = new(ChapterActionStatus.NotFound, "no chapter record for this item");

    private static ChapterActionResult Conflict(string message) => new(ChapterActionStatus.Conflict, message);

    /// <summary>What we saved is ours: the chapters of an Applied or Superseded record, or the ones carried on.</summary>
    private static List<AlignedChapter>? OursIn(ChapterState? state) =>
        state is { Status: ChapterStatus.Applied or ChapterStatus.Superseded } ? state.Chapters : state?.SavedChapters;

    private SidecarResult ReadSidecar(string videoPath)
    {
        try
        {
            return SidecarChapterReader.Read(videoPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Settles in review; once it reads, the real hash differs, so the item is looked at again. The
            // details (with paths) go to the log, not to the record the settings page shows.
            _logger.LogWarning(ex, "Chapters: can't read the sidecar for {File}", Path.GetFileName(videoPath));
            return new SidecarResult(SidecarStatus.Invalid, UnreadableSidecarHash, Array.Empty<AlignedChapter>(), $"sidecar can't be read ({ex.GetType().Name})");
        }
    }

    private static bool Changed(Job job) =>
        FileFingerprint.Of(job.Path) is not { } now || now with { SidecarHash = job.Fingerprint.SidecarHash } != job.Fingerprint;

    private async Task<ChapterActionResult> WithItemAsync(Guid itemId, Func<Task<ChapterActionResult>> action)
    {
        if (!_inFlight.TryAdd(itemId, 0))
        {
            return new ChapterActionResult(ChapterActionStatus.Busy, "the show is being processed; try again in a minute");
        }

        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(itemId, out _);
        }
    }

    private async Task<ChapterOutcome> ProcessLockedAsync(Video item, RunBudget? budget, CancellationToken cancellationToken)
    {
        long runtime = item.RunTimeTicks ?? 0;
        if (runtime <= 0 || ShowDateOf(item) is not { } showDate)
        {
            return new ChapterOutcome(ChapterOutcomeKind.NotEligible);
        }

        if (FileFingerprint.Of(item.Path) is not { } file)
        {
            return new ChapterOutcome(ChapterOutcomeKind.NotEligible, "file-missing");
        }

        // Before the Applied fast path: adding, editing or removing a sidecar changes the fingerprint.
        var sidecar = ReadSidecar(item.Path);
        var fingerprint = file with { SidecarHash = sidecar.Hash };
        var state = await _store.ReadAsync(item.Id, cancellationToken).ConfigureAwait(false);

        // What we saved is still ours to replace after the file changes, until Jellyfin re-probes it.
        var ours = OursIn(state);
        if (state is not null && state.Fingerprint != fingerprint)
        {
            state = null; // a new file or sidecar, so an old result (even Ignored) no longer applies
        }

        var job = new Job(item, item.Path, showDate.Date, fingerprint, runtime, state, ours, sidecar, _time.GetUtcNow().UtcDateTime);
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
            // Every later caller waits too, not just this item: the pause is the service's.
            var wait = ex.RetryAfter is { } after && after > TimeSpan.Zero ? after : DefaultRateLimitWait;
            _phishInPausedUntil = job.Now + wait;
            await RecordAsync(job, RateLimited(job, $"rate-limited ({(int)ex.StatusCode})", _phishInPausedUntil), cancellationToken).ConfigureAwait(false);
            Log(job, "PENDING", $"phish.in rate limit; paused until {_phishInPausedUntil:u}");
            return new ChapterOutcome(ChapterOutcomeKind.RateLimited, ex.Message, _phishInPausedUntil);
        }
        catch (Exception ex)
        {
            return await FailAsync(job, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChapterOutcome> DecideAsync(Job job, RunBudget? budget, CancellationToken cancellationToken)
    {
        var state = job.Previous;
        if (state is { RetryRequested: true })
        {
            job.Previous = state = null; // the admin asked for a fresh start; what we saved stays ours (job.Ours)
        }

        if (state is { Status: ChapterStatus.Ignored })
        {
            return new ChapterOutcome(ChapterOutcomeKind.Unchanged, "ignored");
        }

        var situation = Classify(job);
        if (state is { Status: ChapterStatus.Applied or ChapterStatus.Superseded })
        {
            if (await KeepAppliedAsync(job, situation, cancellationToken).ConfigureAwait(false) is { } kept)
            {
                return kept;
            }

            job.Previous = state = null; // the cached rows no longer fit this file; align it again
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

        if (job.Sidecar.Status != SidecarStatus.Missing)
        {
            return await ApplySidecarAsync(job, cancellationToken).ConfigureAwait(false);
        }

        // Only checked before aligning: an applied result was matched against this date's audio already.
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

        if (state is { Status: ChapterStatus.Pending or ChapterStatus.Failed, NextAttemptUtc: { } next } && next > job.Now)
        {
            return new ChapterOutcome(ChapterOutcomeKind.Unchanged, $"waiting until {next:u}", next);
        }

        if (state is { Status: ChapterStatus.Skipped or ChapterStatus.NeedsReview } && state.AlgorithmVersion == AlgorithmVersion)
        {
            return new ChapterOutcome(ChapterOutcomeKind.Unchanged, state.Reason);
        }

        return await AlignAsync(job, budget, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Handles an item whose record is Applied or Superseded; null means align it again.</summary>
    private async Task<ChapterOutcome?> KeepAppliedAsync(Job job, ChapterSituation situation, CancellationToken cancellationToken)
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
                    Log(job, "REALIGN", "cached chapters no longer fit: " + invalid);
                    return null;
                }

                _chapters.SaveChapters(job.Item, ChapterDetector.ToChapterInfos(state.Chapters));

                // Not cancellable once saved: a lost record would make our chapters look like anyone's.
                await RecordAsync(job, Copy(state, ChapterStatus.Applied, null, job.Now), CancellationToken.None).ConfigureAwait(false);
                Log(job, "REAPPLY", $"{state.Chapters.Count} ch restored after Jellyfin dropped them");
                return new ChapterOutcome(ChapterOutcomeKind.Reapplied);
        }
    }

    /// <summary>The sidecar is the admin's word for this item: never aligned, and it needs no show date check.</summary>
    private async Task<ChapterOutcome> ApplySidecarAsync(Job job, CancellationToken cancellationToken)
    {
        var sidecar = job.Sidecar;
        var problem = sidecar.Status == SidecarStatus.Invalid
            ? sidecar.Error
            : ChapterValidator.Validate(sidecar.Chapters, job.Runtime) is { } invalid ? "sidecar: " + invalid : null;
        if (problem is not null)
        {
            return job.Previous is { Status: ChapterStatus.NeedsReview } p && p.Reason == problem
                ? new ChapterOutcome(ChapterOutcomeKind.Unchanged, problem)
                : await SettleAsync(job, ChapterStatus.NeedsReview, problem, null, cancellationToken).ConfigureAwait(false);
        }

        _chapters.SaveChapters(job.Item, ChapterDetector.ToChapterInfos(sidecar.Chapters));
        var state = New(job, ChapterStatus.Applied, null, null);
        state.Chapters = sidecar.Chapters.ToList();
        state.Source = ChapterSource.Sidecar;

        // Not cancellable once saved: a lost record would make our chapters look like anyone's.
        await RecordAsync(job, state, CancellationToken.None).ConfigureAwait(false);
        Log(job, "SIDECAR", $"{sidecar.Chapters.Count} ch from {Path.GetFileName(SidecarChapterReader.PathFor(job.Path))}");
        return new ChapterOutcome(ChapterOutcomeKind.Applied, "sidecar");
    }

    private async Task<ChapterOutcome> AlignAsync(Job job, RunBudget? budget, CancellationToken cancellationToken)
    {
        if (StillWriting(job.Fingerprint.ModifiedUtc, job.Now))
        {
            return await FileChangingAsync(job, cancellationToken).ConfigureAwait(false);
        }

        double runtimeSeconds = job.Runtime / (double)TimeSpan.TicksPerSecond;
        if (runtimeSeconds < AlignmentConstants.MinVideoSeconds)
        {
            return await SettleAsync(job, ChapterStatus.Skipped, AlignmentGates.CheckVideoLength(runtimeSeconds), null, cancellationToken).ConfigureAwait(false);
        }

        if (job.Now < _phishInPausedUntil)
        {
            return new ChapterOutcome(ChapterOutcomeKind.RateLimited, $"phish.in paused until {_phishInPausedUntil:u}", _phishInPausedUntil);
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

        // Templates before the video: they're small and cached, so a phish.in error or rate limit fails
        // fast, and its retry doesn't read the whole video again.
        var templates = await _audio.GetTemplatesAsync(show, cancellationToken).ConfigureAwait(false);
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

        var align = Stopwatch.StartNew();
        var tracks = show.Tracks.Select((t, i) => new AlignmentTrack(t.Title, t.DurationSeconds, templates.Templates[i], t.SetName)).ToList();
        var result = ConsensusAligner.Align(video, tracks, AlignmentOptions.PerSet);
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

        // Not cancellable once saved: a lost record would make our chapters look like anyone's.
        var applied = New(job, ChapterStatus.Applied, null, result);
        applied.Source = ChapterSource.Aligned;
        await RecordAsync(job, applied, CancellationToken.None).ConfigureAwait(false);
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

    private ChapterSituation Classify(Job job) =>
        ChapterDetector.Classify(_chapters.GetChapters(job.Item.Id), job.Ours, DummiesFor(job.Runtime));

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

        // An error in between doesn't restart the 60-day clock.
        var first = job.Previous is { Status: ChapterStatus.Pending or ChapterStatus.Failed } q && q.FirstPendingUtc is { } since ? since : job.Now;
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
        return new ChapterOutcome(ChapterOutcomeKind.Pending, detail, next);
    }

    private async Task<ChapterOutcome> FileChangingAsync(Job job, CancellationToken cancellationToken)
    {
        // The latest stat goes in the record, so a file that settles keeps its retry time.
        var now = FileFingerprint.Of(job.Path);
        if (now is null)
        {
            return new ChapterOutcome(ChapterOutcomeKind.NotEligible, "file-missing");
        }

        job.Fingerprint = now with { SidecarHash = job.Fingerprint.SidecarHash };
        var retry = job.Now + MinFileAge;
        await RecordAsync(job, PendingCarry(job, FileChangingReason, retry), cancellationToken).ConfigureAwait(false);
        Log(job, "PENDING", FileChangingReason);
        return new ChapterOutcome(ChapterOutcomeKind.Pending, FileChangingReason, retry);
    }

    private async Task<ChapterOutcome> FailAsync(Job job, Exception ex, CancellationToken cancellationToken)
    {
        if (job.Previous is { Status: ChapterStatus.Applied or ChapterStatus.Superseded })
        {
            // Keep the good result; the next pass tries the re-apply again.
            _logger.LogError(ex, "Chapters FAILED {Date} {File}; record left as {Status}", Date(job), FileName(job), job.Previous.Status);
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
        state.FirstPendingUtc = job.Previous is { Status: ChapterStatus.Pending or ChapterStatus.Failed } q ? q.FirstPendingUtc : null;
        _logger.LogError(ex, "Chapters FAILED {Date} {File}: attempt {Attempts}, next {Next}", Date(job), FileName(job), attempts, next?.ToString("u", CultureInfo.InvariantCulture) ?? "review");
        try
        {
            await RecordAsync(job, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception writeError) when (writeError is not OperationCanceledException)
        {
            _logger.LogError(writeError, "Chapters: couldn't write the record for {File}", FileName(job));
        }

        return new ChapterOutcome(ChapterOutcomeKind.Failed, reason, next);
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
        SavedChapters = status == ChapterStatus.Applied ? null : job.Ours?.ToList(),
        UpdatedUtc = job.Now,
    };

    /// <summary>A Pending record that doesn't count as a check: the attempts and first-pending time carry over.</summary>
    private static ChapterState PendingCarry(Job job, string reason, DateTime next)
    {
        var state = New(job, ChapterStatus.Pending, reason, null);
        if (job.Previous is { Status: ChapterStatus.Pending or ChapterStatus.Failed } p)
        {
            state.Attempts = p.Status == ChapterStatus.Pending ? p.Attempts : 0;
            state.FirstPendingUtc = p.FirstPendingUtc;
        }

        state.NextAttemptUtc = next;
        return state;
    }

    /// <summary>A rate limit isn't a check or a failure: a Pending or Failed record keeps its status and counters.</summary>
    private static ChapterState RateLimited(Job job, string reason, DateTime next)
    {
        var status = job.Previous is { Status: ChapterStatus.Failed } ? ChapterStatus.Failed : ChapterStatus.Pending;
        var state = New(job, status, reason, null);
        if (job.Previous is { Status: ChapterStatus.Pending or ChapterStatus.Failed } p)
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
        Source = state.Source,
        SavedChapters = state.SavedChapters,
        UpdatedUtc = now,
    };

    private static string Date(Job job) => job.ShowDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FileName(Job job) => Path.GetFileName(job.Path);

    private void Log(Job job, string verb, string detail) =>
        _logger.LogInformation("Chapters {Verb} {Date} {File}: {Detail}", verb, Date(job), FileName(job), detail);

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
        _logger.LogDebug("Chapters for {Date} {File}:\n{Table}", Date(job), FileName(job), string.Join('\n', lines));
    }

    private sealed class Job(Video item, string path, DateTime showDate, FileFingerprint fingerprint, long runtime, ChapterState? previous, IReadOnlyList<AlignedChapter>? ours, SidecarResult sidecar, DateTime now)
    {
        public Video Item { get; } = item;

        public string Path { get; } = path;

        public DateTime ShowDate { get; } = showDate;

        public FileFingerprint Fingerprint { get; set; } = fingerprint;

        public long Runtime { get; } = runtime;

        public ChapterState? Previous { get; set; } = previous;

        /// <summary>Gets the chapters this plugin last saved for the item, even for an older version of the file.</summary>
        public IReadOnlyList<AlignedChapter>? Ours { get; } = ours;

        /// <summary>Gets the sidecar next to the video, read once per pass.</summary>
        public SidecarResult Sidecar { get; } = sidecar;

        public DateTime Now { get; } = now;
    }
}
