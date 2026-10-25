using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The per-item state file is what makes the feature idempotent. It lives in a stable folder,
/// is written atomically, and a damaged or unknown-format file reads as "no record".
/// </summary>
public class ChapterStateStoreTests : IDisposable
{
    private static readonly Guid Item = Guid.Parse("6f1c5a2e-41c3-4f0b-9d8e-2b7c1d3e4f50");
    private readonly string _root = Directory.CreateTempSubdirectory("phish-state-").FullName;
    private readonly string _dir;

    public ChapterStateStoreTests()
    {
        _dir = Path.Combine(_root, "chapters");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Write_ThenRead_ShouldRoundTripEveryField()
    {
        var store = new ChapterStateStore(_dir);
        var state = Sample();

        await store.WriteAsync(Item, state, CancellationToken.None);
        var read = await store.ReadAsync(Item, CancellationToken.None);

        read.Should().BeEquivalentTo(state);
        read!.Fingerprint.Should().Be(state.Fingerprint);
    }

    [Fact]
    public async Task Write_ShouldNameTheFileByItemId_AndLeaveNoTempFiles()
    {
        var store = new ChapterStateStore(_dir);

        await store.WriteAsync(Item, Sample(), CancellationToken.None);
        await store.WriteAsync(Item, Sample(ChapterStatus.Superseded), CancellationToken.None);

        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().Equal("6f1c5a2e41c34f0b9d8e2b7c1d3e4f50.json");
        (await store.ReadAsync(Item, CancellationToken.None))!.Status.Should().Be(ChapterStatus.Superseded);
        store.KnownItemIds().Should().Equal(Item);
    }

    [Fact]
    public async Task Read_ShouldMiss_WhenThereIsNoRecord()
    {
        (await new ChapterStateStore(_dir).ReadAsync(Item, CancellationToken.None)).Should().BeNull();
        new ChapterStateStore(_dir).KnownItemIds().Should().BeEmpty();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{"schemaVersion":99,"status":"Applied"}""")]
    [InlineData("""{"schemaVersion":1,"status":"Bogus"}""")]
    public async Task Read_ShouldMiss_ForADamagedOrUnknownRecord(string json)
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, Item.ToString("N") + ".json"), json);

        (await new ChapterStateStore(_dir).ReadAsync(Item, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Read_ShouldMiss_ForAFileFarLargerThanAnyRecord()
    {
        Directory.CreateDirectory(_dir);
        await using (var f = File.Create(Path.Combine(_dir, Item.ToString("N") + ".json")))
        {
            f.SetLength(16L * 1024 * 1024);
        }

        (await new ChapterStateStore(_dir).ReadAsync(Item, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReadAll_ShouldReturnEveryReadableRecord()
    {
        var store = new ChapterStateStore(_dir);
        var other = Guid.NewGuid();
        await store.WriteAsync(Item, Sample(), CancellationToken.None);
        await store.WriteAsync(other, Sample(ChapterStatus.NeedsReview), CancellationToken.None);
        File.WriteAllText(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"), "not json");

        var all = await store.ReadAllAsync(CancellationToken.None);

        all.Select(p => (p.Key, p.Value.Status)).Should().BeEquivalentTo(new[] { (Item, ChapterStatus.Applied), (other, ChapterStatus.NeedsReview) });
    }

    [Fact]
    public async Task Read_ShouldDefaultTheNewerFields_ForAPhaseThreeRecord()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, Item.ToString("N") + ".json"), """{"schemaVersion":1,"status":"Applied","chapters":[]}""");

        var read = await new ChapterStateStore(_dir).ReadAsync(Item, CancellationToken.None);

        read!.Source.Should().BeNull();
        read.SavedChapters.Should().BeNull();
        read.RetryRequested.Should().BeFalse();
    }

    [Fact]
    public void KnownItemIds_ShouldIgnoreOtherFiles()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "notes.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, Item.ToString("N") + ".json.1234.tmp"), "{}");

        new ChapterStateStore(_dir).KnownItemIds().Should().BeEmpty();
    }

    [Fact]
    public async Task Fingerprint_ShouldChange_WhenTheFileIsRewritten()
    {
        var path = Path.Combine(_root, "ph2026-07-07.mkv");
        await File.WriteAllTextAsync(path, "video");
        var before = FileFingerprint.Of(path);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        var touched = FileFingerprint.Of(path);
        await File.AppendAllTextAsync(path, "more");

        before.Should().NotBeNull();
        touched.Should().NotBe(before);
        FileFingerprint.Of(path).Should().NotBe(touched);
        FileFingerprint.Of(Path.Combine(_root, "gone.mkv")).Should().BeNull();
    }

    private static ChapterState Sample(ChapterStatus status = ChapterStatus.Applied) => new()
    {
        Status = status,
        Reason = null,
        AlgorithmVersion = 1,
        Fingerprint = new FileFingerprint("/Concerts/ph2026-07-07.mkv", 9_600_000_000, new DateTime(2026, 7, 9, 3, 4, 5, 678, DateTimeKind.Utc).AddTicks(9), "ab12"),
        ShowDate = new DateTime(2026, 7, 7),
        Chapters = [new AlignedChapter("Chalk Dust Torture", 12.35, 0.81), new AlignedChapter("Ghost", 604.5, null)],
        Source = ChapterSource.Sidecar,
        SavedChapters = [new AlignedChapter("Tweezer", 1.0, 0.5)],
        RetryRequested = true,
        MedianConfidence = 0.73,
        LowConfidenceCount = 0,
        Attempts = 1,
        FirstPendingUtc = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
        NextAttemptUtc = null,
        UpdatedUtc = new DateTime(2026, 10, 23, 6, 0, 0, DateTimeKind.Utc),
    };
}
