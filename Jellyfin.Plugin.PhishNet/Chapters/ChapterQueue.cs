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
/// </summary>
public sealed class ChapterQueue : IHostedService, IDisposable
{
    /// <summary>How long an item must go without updates before it is processed.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(2);

    /// <summary>How often the worker looks for due items.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly ILibraryManager _library;
    private readonly IChapterMarkerService _service;
    private readonly Func<PluginConfiguration> _config;
    private readonly TimeProvider _time;
    private readonly ILogger<ChapterQueue> _logger;
    private readonly ConcurrentDictionary<Guid, DateTime> _lastUpdate = new();
    private CancellationTokenSource? _stopping;
    private Task? _worker;
    private DateTime _pausedUntil;
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
    public int Count => _lastUpdate.Count;

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
        if (nowUtc < _pausedUntil)
        {
            return;
        }

        foreach (var (id, last) in _lastUpdate.OrderBy(p => p.Value).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nowUtc - last < QuietPeriod)
            {
                continue;
            }

            if (_library.GetItemById(id) is not Video video || FileFingerprint.Of(video.Path) is not { } file)
            {
                _lastUpdate.TryRemove(id, out _);
                continue;
            }

            if (nowUtc - file.ModifiedUtc < ChapterMarkerService.MinFileAge)
            {
                continue; // still being written
            }

            // Removed only if no newer update arrived meanwhile; a newer one waits its own quiet period.
            if (!_lastUpdate.TryRemove(new KeyValuePair<Guid, DateTime>(id, last)) || !Enabled())
            {
                continue;
            }

            try
            {
                var outcome = await _service.ProcessAsync(video, null, cancellationToken).ConfigureAwait(false);
                if (outcome.Kind == ChapterOutcomeKind.RateLimited)
                {
                    _pausedUntil = nowUtc + ChapterMarkerService.DefaultRateLimitWait;
                    _logger.LogInformation("Chapters: phish.in asked us to slow down; library changes wait until {Until:u}", _pausedUntil);
                    return;
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

    private bool Enabled()
    {
        var config = _config();
        return config.EnableChapterMarkers && config.ChapterMarkersOnLibraryChanges;
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie movie && movie.RunTimeTicks > 0 && Enabled() && ChapterMarkerService.ShowDateOf(movie) is not null)
        {
            _lastUpdate[movie.Id] = _time.GetUtcNow().UtcDateTime;
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
