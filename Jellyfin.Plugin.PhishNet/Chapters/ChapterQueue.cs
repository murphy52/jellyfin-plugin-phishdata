using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>
/// Turns library updates into chapter work (plan §4.3). <c>ItemUpdated</c> fires after a refresh has
/// probed the file, so the item has a runtime; <c>ItemAdded</c> fires before that and is ignored. The
/// event handler only records the item. A worker, ticking every minute, hands an item to the service once
/// no update has arrived for <see cref="QuietPeriod"/> and its file is at least
/// <see cref="ChapterMarkerService.MinFileAge"/> old, which covers copies in progress and scan bursts.
/// An item the service wants to retry (not on phish.in yet, an error, a rate limit) stays queued until
/// then, so a new show isn't left until the nightly task. Library changes share one rolling 24-hour budget
/// built from the per-run settings, so a "Replace all metadata" on a big library can't read every show at
/// once; what doesn't fit is left to the scheduled task.
/// </summary>
public sealed class ChapterQueue : IHostedService, IDisposable
{
    /// <summary>How long an item must go without updates before it is processed.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(2);

    /// <summary>How often the worker looks for due items.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>How long one library-change budget lasts.</summary>
    public static readonly TimeSpan BudgetWindow = TimeSpan.FromDays(1);

    private readonly ILibraryManager _library;
    private readonly IChapterMarkerService _service;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _time;
    private readonly ILogger<ChapterQueue> _logger;
    private readonly ConcurrentDictionary<Guid, DateTime> _due = new();
    private CancellationTokenSource? _stopping;
    private Task? _worker;
    private RunBudget? _budget;
    private DateTime _budgetEndsUtc;
    private bool _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterQueue"/> class.
    /// </summary>
    /// <param name="library">The library manager.</param>
    /// <param name="service">The chapter service.</param>
    /// <param name="config">Reads the current plugin configuration.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public ChapterQueue(ILibraryManager library, IChapterMarkerService service, Func<PluginConfiguration> config, TimeProvider time, ILogger<ChapterQueue> logger)
    {
        _library = library;
        _service = service;
        _config = config;
        _time = time;
        _logger = logger;
    }

    /// <summary>Gets how many items are waiting.</summary>
    public int Count => _due.Count;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemUpdated += OnItemUpdated;
        _subscribed = true;
        _stopping = new CancellationTokenSource();
        _worker = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        if (_stopping is null || _worker is null)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    /// <summary>
    /// Processes every item that is due, one at a time.
    /// </summary>
    /// <param name="nowUtc">Now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the due items are done.</returns>
    public async Task ProcessDueAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var (id, due) in _due.OrderBy(p => p.Value).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nowUtc < due)
            {
                continue;
            }

            if (_library.GetItemById(id) is not Video video || FileFingerprint.Of(video.Path) is not { } file)
            {
                _due.TryRemove(id, out _);
                continue;
            }

            if (ChapterMarkerService.StillWriting(file.ModifiedUtc, nowUtc))
            {
                continue;
            }

            // Removed only if no newer update arrived meanwhile; a newer one waits its own quiet period.
            if (!_due.TryRemove(new KeyValuePair<Guid, DateTime>(id, due)) || !Enabled())
            {
                continue;
            }

            try
            {
                var outcome = await _service.ProcessAsync(video, BudgetFor(nowUtc), cancellationToken).ConfigureAwait(false);
                if (outcome.RetryAtUtc is { } retry && outcome.Kind is ChapterOutcomeKind.Pending or ChapterOutcomeKind.Failed or ChapterOutcomeKind.RateLimited)
                {
                    _due.TryAdd(id, retry); // an update that arrived meanwhile keeps its own, sooner time
                }

                if (outcome.Kind == ChapterOutcomeKind.RateLimited)
                {
                    return; // the rest would only be told the same
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Chapters: error processing {Name}", video.Name);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
        _stopping?.Cancel();
        _stopping?.Dispose();
    }

    private RunBudget BudgetFor(DateTime nowUtc)
    {
        if (_budget is null || nowUtc >= _budgetEndsUtc)
        {
            var config = _config();
            _budget = new RunBudget(Math.Max(0, config.ChapterMarkersMaxShowsPerRun), Math.Max(0, config.ChapterMarkersMaxGbPerRun) * 1024L * 1024 * 1024);
            _budgetEndsUtc = nowUtc + BudgetWindow;
        }

        return _budget;
    }

    private bool Enabled()
    {
        var config = _config();
        return config.EnableChapterMarkers && config.ChapterMarkersOnLibraryChanges;
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie movie && movie.RunTimeTicks > 0 && Enabled() && ChapterMarkerService.ShowDateOf(movie) is not null)
        {
            _due[movie.Id] = _time.GetUtcNow().UtcDateTime + QuietPeriod;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            _library.ItemUpdated -= OnItemUpdated;
            _subscribed = false;
        }
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, _time, stopping).ConfigureAwait(false);
                await ProcessDueAsync(_time.GetUtcNow().UtcDateTime, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chapters: queue worker error");
            }
        }
    }
}
