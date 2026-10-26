using FluentAssertions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The scheduled task awaits its own work, so Run now stays active until it's done, and it stops at the
/// run's budgets or a rate limit.
/// </summary>
public class ChapterMarkerTaskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chapter-task-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ILibraryManager> _library = new();
    private readonly FakeService _service = new();
    private readonly ChapterStateStore _store;
    private readonly PluginConfiguration _config = new() { EnableChapterMarkers = true };
    private readonly List<BaseItem> _items = new();
    private readonly ChapterMarkerTask _task;

    public ChapterMarkerTaskTests()
    {
        _store = new ChapterStateStore(_dir);
        InternalItemsQuery? seen = null;
        _library.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => seen = q)
            .Returns(() => _items);
        _task = new ChapterMarkerTask(_library.Object, _service, _store, () => _config, NullLogger<ChapterMarkerTask>.Instance);
        Query = () => seen;
    }

    private Func<InternalItemsQuery?> Query { get; }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ShouldDoNothing_WhenChapterMarkersAreOff()
    {
        _config.EnableChapterMarkers = false;
        _items.Add(Show("ph2026-07-07"));

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Processed.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ShouldAskForMoviesRecursively_AndProcessOnlyDatedPhishShows()
    {
        var show = Show("ph2026-07-07");
        _items.AddRange([show, new Movie { Id = Guid.NewGuid(), Name = "Heat", Path = "/media/Heat.mkv", RunTimeTicks = 1 }]);

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Query()!.IncludeItemTypes.Should().Equal(BaseItemKind.Movie);
        Query()!.Recursive.Should().BeTrue();
        _service.Processed.Should().Equal(show.Id);
    }

    [Fact]
    public async Task Execute_ShouldProcessItemsWithARecordFirst()
    {
        var fresh = Show("ph2026-07-07");
        var known = Show("ph2026-07-08");
        _items.AddRange([fresh, known]);
        await _store.WriteAsync(known.Id, new ChapterState { Status = ChapterStatus.Applied }, CancellationToken.None);

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Processed.Should().Equal(known.Id, fresh.Id);
    }

    [Fact]
    public async Task Execute_ShouldProcessNewestShowsFirst_WithinKnownAndNewGroups()
    {
        // Known group: release date desc, the show date from the file when PremiereDate is null.
        var knownOld = Show("ph1997-11-22", premiere: new DateTime(1997, 11, 22));
        var knownNoPremiere = Show("ph2003-07-29");
        var knownNew = Show("ph2019-12-31", premiere: new DateTime(2019, 12, 31));

        // New group: two videos of one show tie on release date and fall back to date added desc.
        var newOld = Show("ph1995-12-31", premiere: new DateTime(1995, 12, 31), added: new DateTime(2026, 1, 1));
        var sameShowEarly = Show("ph2024-08-31", premiere: new DateTime(2024, 8, 31), added: new DateTime(2026, 3, 1));
        var sameShowLate = Show("ph2024-08-31", added: new DateTime(2026, 9, 1));
        var newNoPremiere = Show("ph2009-03-06", added: new DateTime(2026, 10, 1));

        _items.AddRange([newOld, knownOld, sameShowEarly, knownNoPremiere, newNoPremiere, sameShowLate, knownNew]);
        foreach (var item in new[] { knownOld, knownNoPremiere, knownNew })
        {
            await _store.WriteAsync(item.Id, new ChapterState { Status = ChapterStatus.Applied }, CancellationToken.None);
        }

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Processed.Should().Equal(
            knownNew.Id,
            knownNoPremiere.Id,
            knownOld.Id,
            sameShowLate.Id,
            sameShowEarly.Id,
            newNoPremiere.Id,
            newOld.Id);
    }

    [Fact]
    public async Task Execute_ShouldPassOneBudgetFromTheSettings()
    {
        _config.ChapterMarkersMaxShowsPerRun = 3;
        _config.ChapterMarkersMaxGbPerRun = 7;
        _items.AddRange([Show("ph2026-07-07"), Show("ph2026-07-08")]);

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Budgets.Should().HaveCount(2).And.OnlyContain(b => ReferenceEquals(b, _service.Budgets[0]));
        _service.Budgets[0]!.ShowsLeft.Should().Be(3);
        _service.Budgets[0]!.BytesLeft.Should().Be(7L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task Execute_ShouldStopSweepingNewShows_OnceTheBudgetIsSpent()
    {
        _config.ChapterMarkersMaxShowsPerRun = 1;
        _service.ChargePerShow = 1;
        var known = Show("ph2026-07-01");
        _items.AddRange([Show("ph2026-07-07"), Show("ph2026-07-08"), known]);
        await _store.WriteAsync(known.Id, new ChapterState { Status = ChapterStatus.Applied }, CancellationToken.None);
        _service.Free.Add(known.Id); // a re-apply reads no video

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Processed.Should().HaveCount(2, "the known show and one new show");
        _service.Processed[0].Should().Be(known.Id);
    }

    [Fact]
    public async Task Execute_ShouldStop_WhenPhishInRateLimits()
    {
        _service.Outcome = new ChapterOutcome(ChapterOutcomeKind.RateLimited);
        _items.AddRange([Show("ph2026-07-07"), Show("ph2026-07-08")]);

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Processed.Should().HaveCount(1);
    }

    [Fact]
    public async Task Execute_ShouldKeepGoing_WhenOneShowThrows()
    {
        var first = Show("ph2026-07-08");
        var second = Show("ph2026-07-07");
        _items.AddRange([first, second]);
        _service.ThrowFor = first.Id;

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        _service.Processed.Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Execute_ShouldNotFinish_UntilItsShowsAreDone()
    {
        var release = new TaskCompletionSource<ChapterOutcome>();
        _service.Pending = release.Task;
        _items.Add(Show("ph2026-07-07"));

        var run = _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
        await Task.Delay(50);
        run.IsCompleted.Should().BeFalse();

        release.SetResult(new ChapterOutcome(ChapterOutcomeKind.Applied));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Execute_ShouldPropagateCancellation()
    {
        using var cts = new CancellationTokenSource();
        _items.AddRange([Show("ph2026-07-07"), Show("ph2026-07-08")]);
        _service.OnProcess = cts.Cancel;

        var act = () => _task.ExecuteAsync(new Progress<double>(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _service.Processed.Should().HaveCount(1);
    }

    [Fact]
    public async Task Execute_ShouldReportProgressUpTo100()
    {
        _items.AddRange([Show("ph2026-07-07"), Show("ph2026-07-08")]);
        var reports = new List<double>();

        await _task.ExecuteAsync(new SyncProgress(reports.Add), CancellationToken.None);

        reports.Should().Contain(50).And.EndWith(100);
    }

    [Fact]
    public void DefaultTrigger_ShouldBeDailyAtFourAm()
    {
        var trigger = _task.GetDefaultTriggers().Single();

        trigger.Type.Should().Be(TaskTriggerInfoType.DailyTrigger);
        trigger.TimeOfDayTicks.Should().Be(TimeSpan.FromHours(4).Ticks);
        _task.Name.Should().Be("Phish: add chapter markers");
    }

    private static Movie Show(string name, DateTime? premiere = null, DateTime? added = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"/media/Phish/{name}.Webcast.mkv",
            RunTimeTicks = TimeSpan.FromHours(3).Ticks,
            PremiereDate = premiere,
            DateCreated = added ?? default,
        };

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class FakeService : IChapterMarkerService
    {
        public List<Guid> Processed { get; } = new();

        public List<RunBudget?> Budgets { get; } = new();

        public ChapterOutcome Outcome { get; set; } = new(ChapterOutcomeKind.Applied);

        public Guid? ThrowFor { get; set; }

        public long ChargePerShow { get; set; }

        public HashSet<Guid> Free { get; } = new();

        public Task<ChapterOutcome>? Pending { get; set; }

        public Action? OnProcess { get; set; }

        public async Task<ChapterOutcome> ProcessAsync(Video item, RunBudget? budget, CancellationToken cancellationToken)
        {
            Processed.Add(item.Id);
            Budgets.Add(budget);
            OnProcess?.Invoke();
            if (item.Id == ThrowFor)
            {
                throw new IOException("boom");
            }

            if (ChargePerShow > 0 && !Free.Contains(item.Id) && budget is not null && budget.CanRead(ChargePerShow))
            {
                budget.Charge(ChargePerShow);
            }

            return Pending is null ? Outcome : await Pending;
        }
    }
}
