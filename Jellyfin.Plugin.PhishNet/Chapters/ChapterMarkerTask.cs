using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>
/// "Phish: add chapter markers" (plan §4.3): re-applies chapters Jellyfin dropped, retries shows whose
/// backoff has passed, then sweeps shows with no record until the run's show or GB budget is spent. Each
/// group goes newest show first (release date, then date added), so a backlog fills from recent shows. It
/// awaits every show, so Run now stays active until the work is done and cancelling it stops the show
/// in progress.
/// </summary>
public sealed class ChapterMarkerTask : IScheduledTask
{
    private const long BytesPerGb = 1024L * 1024 * 1024;

    private readonly ILibraryManager _library;
    private readonly IChapterMarkerService _service;
    private readonly ChapterStateStore _store;
    private readonly Func<PluginConfiguration> _config;
    private readonly ILogger<ChapterMarkerTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterMarkerTask"/> class.
    /// </summary>
    /// <param name="library">The library manager.</param>
    /// <param name="service">The chapter service.</param>
    /// <param name="store">Per-item records, to put known items first.</param>
    /// <param name="config">Reads the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    public ChapterMarkerTask(ILibraryManager library, IChapterMarkerService service, ChapterStateStore store, Func<PluginConfiguration> config, ILogger<ChapterMarkerTask> logger)
    {
        _library = library;
        _service = service;
        _store = store;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Phish: add chapter markers";

    /// <inheritdoc />
    public string Key => "PhishDataChapterMarkers";

    /// <inheritdoc />
    public string Description => "Adds song chapter markers to Phish show videos, using phish.in track audio. Off unless enabled in the Phish Data settings.";

    /// <inheritdoc />
    public string Category => Constants.PluginName;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = _config();
        if (!config.EnableChapterMarkers)
        {
            _logger.LogInformation("Chapters: chapter markers are off in the Phish Data settings; nothing to do");
            progress.Report(100);
            return;
        }

        var known = _store.KnownItemIds().ToHashSet();
        var shows = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Movie], Recursive = true })
            .OfType<Video>()
            .Where(v => v.RunTimeTicks > 0)
            .Select(v => (Video: v, ShowDate: ChapterMarkerService.ShowDateOf(v)))
            .Where(s => s.ShowDate is not null)
            .OrderBy(s => known.Contains(s.Video.Id) ? 0 : 1)
            .ThenByDescending(s => s.Video.PremiereDate is { } premiere ? ChapterMarkerService.ReleaseDay(premiere, TimeZoneInfo.Local) : s.ShowDate!.Value.Date) // newest show first
            .ThenByDescending(s => s.Video.DateCreated) // then the newest copy of a show
            .Select(s => s.Video)
            .ToList();

        var budget = new RunBudget(Math.Max(0, config.ChapterMarkersMaxShowsPerRun), Math.Max(0, config.ChapterMarkersMaxGbPerRun) * BytesPerGb);
        var tally = new Dictionary<ChapterOutcomeKind, int>();
        string stoppedBy = "done";
        for (int i = 0; i < shows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var show = shows[i];
            if (budget.Exhausted && !known.Contains(show.Id))
            {
                stoppedBy = "budget spent";
                break;
            }

            ChapterOutcomeKind kind;
            try
            {
                kind = (await _service.ProcessAsync(show, budget, cancellationToken).ConfigureAwait(false)).Kind;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Chapters: error processing {Name}", show.Name);
                kind = ChapterOutcomeKind.Failed;
            }

            tally[kind] = tally.GetValueOrDefault(kind) + 1;
            progress.Report(100.0 * (i + 1) / shows.Count);
            if (kind == ChapterOutcomeKind.RateLimited)
            {
                stoppedBy = "phish.in rate limit";
                break;
            }
        }

        _logger.LogInformation(
            "Chapters run ({Stopped}): {Shows} shows, {Tally}; read {Gb:0.0} GB",
            stoppedBy,
            shows.Count,
            tally.Count == 0 ? "nothing processed" : string.Join(", ", tally.OrderBy(t => t.Key).Select(t => $"{t.Value} {t.Key}")),
            budget.BytesRead / (double)BytesPerGb);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks },
    ];
}
