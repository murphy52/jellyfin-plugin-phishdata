using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

public class ChapterStatusReportTests
{
    private static readonly List<AlignedChapter> Rows = [new AlignedChapter("Tweezer", 10, 0.3)];

    [Fact]
    public void Build_ShouldCountEveryStatus_IncludingEmptyOnes()
    {
        var report = ChapterStatusReport.Build(
            [Record(ChapterStatus.Applied), Record(ChapterStatus.Applied), Record(ChapterStatus.Pending)],
            _ => null,
            enabled: true);

        report.Enabled.Should().BeTrue();
        report.Counts.Should().HaveCount(Enum.GetValues<ChapterStatus>().Length);
        report.Counts["Applied"].Should().Be(2);
        report.Counts["Pending"].Should().Be(1);
        report.Counts["NeedsReview"].Should().Be(0);
        report.Items.Should().BeEmpty("applied and pending shows need nothing from the admin");
    }

    [Fact]
    public void Build_ShouldListReviewThenFailedThenIgnored_NewestShowFirst()
    {
        var report = ChapterStatusReport.Build(
            [
                Record(ChapterStatus.Ignored, "2026-07-01"),
                Record(ChapterStatus.NeedsReview, "2025-12-31"),
                Record(ChapterStatus.Failed, "2026-07-05"),
                Record(ChapterStatus.NeedsReview, "2026-07-07"),
            ],
            _ => null,
            enabled: false);

        report.Items.Select(i => (i.Status, i.ShowDate)).Should().Equal(
            ("NeedsReview", "2026-07-07"),
            ("NeedsReview", "2025-12-31"),
            ("Failed", "2026-07-05"),
            ("Ignored", "2026-07-01"));
    }

    [Fact]
    public void Build_ShouldOfferApplyAnyway_OnlyForAReviewItemWithRowsThatIsStillInTheLibrary()
    {
        var withRows = Record(ChapterStatus.NeedsReview, rows: Rows);
        var withoutRows = Record(ChapterStatus.NeedsReview);
        var gone = Record(ChapterStatus.NeedsReview, rows: Rows);
        var failed = Record(ChapterStatus.Failed, rows: Rows);
        var items = new Dictionary<Guid, BaseItem> { [withRows.Key] = Movie(), [withoutRows.Key] = Movie(), [failed.Key] = Movie() };

        var report = ChapterStatusReport.Build([withRows, withoutRows, gone, failed], id => items.GetValueOrDefault(id), enabled: true);

        report.Items.ToDictionary(i => i.ItemId, i => i.CanApplyAnyway).Should().Equal(new Dictionary<Guid, bool>
        {
            [withRows.Key] = true,
            [withoutRows.Key] = false,
            [gone.Key] = false,
            [failed.Key] = false,
        });
    }

    [Fact]
    public void Build_ShouldShowTheFileNameOnly_AndFallBackToTheRecordForAGoneItem()
    {
        var present = Record(ChapterStatus.NeedsReview, reason: "low-confidence (median 0.31)", rows: Rows);
        var gone = Record(ChapterStatus.Failed);
        var movie = Movie();

        var report = ChapterStatusReport.Build([present, gone], id => id == present.Key ? movie : null, enabled: true);

        var row = report.Items.Single(i => i.ItemId == present.Key);
        row.Name.Should().Be("Phish 2026-07-07");
        row.FileName.Should().Be("ph2026-07-07.mkv");
        row.InLibrary.Should().BeTrue();
        row.Reason.Should().Be("low-confidence (median 0.31)");
        row.MedianConfidence.Should().Be(0.31);
        row.LowConfidenceCount.Should().Be(2);
        row.ChapterCount.Should().Be(1);

        var missing = report.Items.Single(i => i.ItemId == gone.Key);
        missing.InLibrary.Should().BeFalse();
        missing.Name.Should().Be("old");
        missing.FileName.Should().Be("old.mkv");
    }

    private static Movie Movie() => new() { Name = "Phish 2026-07-07", Path = "/Concerts/Phish/ph2026-07-07.mkv" };

    private static KeyValuePair<Guid, ChapterState> Record(ChapterStatus status, string showDate = "2026-07-07", string? reason = null, List<AlignedChapter>? rows = null) =>
        new(
            Guid.NewGuid(),
            new ChapterState
            {
                Status = status,
                Reason = reason,
                ShowDate = DateTime.Parse(showDate, System.Globalization.CultureInfo.InvariantCulture),
                Fingerprint = new FileFingerprint("/Concerts/Phish/old.mkv", 1, DateTime.UnixEpoch),
                Chapters = rows ?? [],
                MedianConfidence = 0.31,
                LowConfidenceCount = 2,
            });
}
