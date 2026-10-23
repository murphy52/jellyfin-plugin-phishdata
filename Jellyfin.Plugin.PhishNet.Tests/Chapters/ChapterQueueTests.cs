using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The queue turns library updates into work for the service, only after the item has gone quiet and its
/// file has stopped changing, and never does I/O in the event handler.
/// </summary>
public class ChapterQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chapter-queue-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ILibraryManager> _library = new();
    private readonly RecordingService _service = new();
    private readonly PluginConfiguration _config = new() { EnableChapterMarkers = true };
    private readonly DateTime _now = new(2026, 10, 23, 4, 0, 0, DateTimeKind.Utc);
    private readonly TestClock _clock;
    private readonly ChapterQueue _queue;
    private readonly Movie _movie;

    public ChapterQueueTests()
    {
        Directory.CreateDirectory(_dir);
        _clock = new TestClock(_now);
        _movie = Show("ph2026-07-07.Webcast.mkv", modified: _now - TimeSpan.FromHours(1));
        _queue = new ChapterQueue(_library.Object, _service, () => _config, _clock, NullLogger<ChapterQueue>.Instance);
    }

    public void Dispose()
    {
        _queue.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task ProcessDue_ShouldProcessAnUpdatedShow_OnceItHasBeenQuietForTwoMinutes()
    {
        await _queue.StartAsync(CancellationToken.None);
        Raise(_movie, at: _now);

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(1), CancellationToken.None);
        _service.Processed.Should().BeEmpty("another update may follow during a scan");

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(2), CancellationToken.None);
        _service.Processed.Should().Equal(_movie.Id);
        _service.Budgets.Should().Equal(new RunBudget?[] { null });

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(10), CancellationToken.None);
        _service.Processed.Should().HaveCount(1, "it left the queue");
    }

    [Fact]
    public async Task ProcessDue_ShouldRestartTheQuietPeriod_OnEachUpdate()
    {
        await _queue.StartAsync(CancellationToken.None);
        Raise(_movie, at: _now);
        Raise(_movie, at: _now + TimeSpan.FromMinutes(1.5));

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(2), CancellationToken.None);

        _service.Processed.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDue_ShouldKeepWaiting_WhileTheFileIsYoungerThanTenMinutes()
    {
        var copying = Show("ph2026-07-08.Webcast.mkv", modified: _now);
        await _queue.StartAsync(CancellationToken.None);
        Raise(copying, at: _now);

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(5), CancellationToken.None);
        _service.Processed.Should().BeEmpty();

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(10), CancellationToken.None);
        _service.Processed.Should().Equal(copying.Id);
    }

    [Fact]
    public async Task ProcessDue_ShouldDropAnItemThatIsGone()
    {
        await _queue.StartAsync(CancellationToken.None);
        Raise(_movie, at: _now);
        _library.Setup(l => l.GetItemById(_movie.Id)).Returns((BaseItem?)null);

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(3), CancellationToken.None);

        _service.Processed.Should().BeEmpty();
        _queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Events_ShouldBeIgnored_WhenTheFeatureOrTheTriggerIsOff()
    {
        await _queue.StartAsync(CancellationToken.None);
        _config.EnableChapterMarkers = false;
        Raise(_movie, at: _now);
        _config.EnableChapterMarkers = true;
        _config.ChapterMarkersOnLibraryChanges = false;
        Raise(_movie, at: _now);

        _queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Events_ShouldOnlyQueueProbedPhishMovies()
    {
        await _queue.StartAsync(CancellationToken.None);
        var unprobed = Show("ph2026-07-09.Webcast.mkv", modified: _now - TimeSpan.FromHours(1));
        unprobed.RunTimeTicks = null;
        var notPhish = new Movie { Id = Guid.NewGuid(), Name = "Heat", Path = Path.Combine(_dir, "Heat (1995).mkv"), RunTimeTicks = TimeSpan.FromHours(3).Ticks };
        var episode = new Episode { Id = Guid.NewGuid(), Name = "ph2026-07-07", Path = _movie.Path, RunTimeTicks = _movie.RunTimeTicks };

        Raise(unprobed, at: _now);
        Raise(notPhish, at: _now);
        Raise(episode, at: _now);

        _queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task Events_ShouldStop_AfterTheQueueStops()
    {
        await _queue.StartAsync(CancellationToken.None);
        await _queue.StopAsync(CancellationToken.None);

        Raise(_movie, at: _now);

        _queue.Count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessDue_ShouldPause_AfterARateLimit()
    {
        var second = Show("ph2026-07-08.Webcast.mkv", modified: _now - TimeSpan.FromHours(1));
        _service.Outcome = new ChapterOutcome(ChapterOutcomeKind.RateLimited);
        await _queue.StartAsync(CancellationToken.None);
        Raise(_movie, at: _now);
        Raise(second, at: _now);

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(3), CancellationToken.None);
        _service.Processed.Should().HaveCount(1);

        _service.Outcome = new ChapterOutcome(ChapterOutcomeKind.Applied);
        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(30), CancellationToken.None);
        _service.Processed.Should().HaveCount(1, "phish.in asked us to wait");

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(3) + ChapterMarkerService.DefaultRateLimitWait, CancellationToken.None);
        _service.Processed.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessDue_ShouldKeepGoing_WhenOneItemThrows()
    {
        var second = Show("ph2026-07-08.Webcast.mkv", modified: _now - TimeSpan.FromHours(1));
        _service.ThrowFor = _movie.Id;
        await _queue.StartAsync(CancellationToken.None);
        Raise(_movie, at: _now);
        Raise(second, at: _now);

        await _queue.ProcessDueAsync(_now + TimeSpan.FromMinutes(3), CancellationToken.None);

        _service.Processed.Should().Contain(second.Id);
    }

    private Movie Show(string fileName, DateTime modified)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, modified);
        var movie = new Movie { Id = Guid.NewGuid(), Name = Path.GetFileNameWithoutExtension(fileName), Path = path, RunTimeTicks = TimeSpan.FromHours(3).Ticks };
        _library.Setup(l => l.GetItemById(movie.Id)).Returns(movie);
        return movie;
    }

    private void Raise(BaseItem item, DateTime at)
    {
        _clock.Now = at;
        _library.Raise(l => l.ItemUpdated += null, _library.Object, new ItemChangeEventArgs { Item = item });
    }

    private sealed class RecordingService : IChapterMarkerService
    {
        public List<Guid> Processed { get; } = new();

        public List<RunBudget?> Budgets { get; } = new();

        public ChapterOutcome Outcome { get; set; } = new(ChapterOutcomeKind.Applied);

        public Guid? ThrowFor { get; set; }

        public Task<ChapterOutcome> ProcessAsync(Video item, RunBudget? budget, CancellationToken cancellationToken)
        {
            Processed.Add(item.Id);
            Budgets.Add(budget);
            return item.Id == ThrowFor ? throw new IOException("boom") : Task.FromResult(Outcome);
        }
    }
}
