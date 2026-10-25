using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>
/// What the settings page shows (plan §4.5): how many shows are in each status, and the shows that wait
/// for the admin.
/// </summary>
public sealed class ChapterStatusReport
{
    /// <summary>The statuses listed one show per row; the rest are only counted.</summary>
    public static readonly IReadOnlyList<ChapterStatus> ListedStatuses = [ChapterStatus.NeedsReview, ChapterStatus.Failed, ChapterStatus.Ignored];

    /// <summary>Gets a value indicating whether chapter markers are on.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the number of shows in each status, every status included.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();

    /// <summary>Gets the shows in review, failed or ignored: review first, newest show first.</summary>
    public IReadOnlyList<ChapterReviewItem> Items { get; init; } = Array.Empty<ChapterReviewItem>();

    /// <summary>
    /// Builds the report.
    /// </summary>
    /// <param name="records">Every record, by item.</param>
    /// <param name="lookup">Finds an item in the library, or null when it's gone.</param>
    /// <param name="enabled">Whether chapter markers are on.</param>
    /// <returns>The report.</returns>
    public static ChapterStatusReport Build(IEnumerable<KeyValuePair<Guid, ChapterState>> records, Func<Guid, BaseItem?> lookup, bool enabled)
    {
        var all = records.ToList();
        var counts = Enum.GetValues<ChapterStatus>().ToDictionary(s => s.ToString(), s => all.Count(r => r.Value.Status == s));
        var items = all
            .Where(r => ListedStatuses.Contains(r.Value.Status))
            .OrderBy(r => IndexOf(r.Value.Status))
            .ThenByDescending(r => r.Value.ShowDate)
            .Select(r => Row(r.Key, r.Value, lookup(r.Key)))
            .ToList();
        return new ChapterStatusReport { Enabled = enabled, Counts = counts, Items = items };
    }

    private static int IndexOf(ChapterStatus status)
    {
        for (int i = 0; i < ListedStatuses.Count; i++)
        {
            if (ListedStatuses[i] == status)
            {
                return i;
            }
        }

        return ListedStatuses.Count;
    }

    private static ChapterReviewItem Row(Guid id, ChapterState state, BaseItem? item)
    {
        var path = item?.Path ?? state.Fingerprint?.Path;
        return new ChapterReviewItem
        {
            ItemId = id,
            Name = item?.Name ?? (path is null ? id.ToString("N") : Path.GetFileNameWithoutExtension(path)),
            FileName = path is null ? null : Path.GetFileName(path),
            InLibrary = item is not null,
            ShowDate = state.ShowDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status = state.Status.ToString(),
            Reason = state.Reason,
            MedianConfidence = state.MedianConfidence,
            LowConfidenceCount = state.LowConfidenceCount,
            ChapterCount = state.Chapters.Count,
            CanApplyAnyway = item is not null && state.Status == ChapterStatus.NeedsReview && state.Chapters.Count > 0,
            RetryRequested = state.RetryRequested,
            UpdatedUtc = state.UpdatedUtc,
            NextAttemptUtc = state.NextAttemptUtc,
        };
    }
}

/// <summary>One show waiting for the admin.</summary>
public sealed class ChapterReviewItem
{
    /// <summary>Gets the Jellyfin item ID.</summary>
    public Guid ItemId { get; init; }

    /// <summary>Gets the item's name, or the file's name when the item is gone.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the video's file name, without its folder.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets a value indicating whether the item is still in the library.</summary>
    public bool InLibrary { get; init; }

    /// <summary>Gets the show date, yyyy-MM-dd.</summary>
    public string? ShowDate { get; init; }

    /// <summary>Gets the status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets why the show has this status.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the alignment's median confidence, when it got that far.</summary>
    public double? MedianConfidence { get; init; }

    /// <summary>Gets how many songs were low-confidence.</summary>
    public int LowConfidenceCount { get; init; }

    /// <summary>Gets how many chapters the record holds.</summary>
    public int ChapterCount { get; init; }

    /// <summary>Gets a value indicating whether Apply anyway is offered: a review item with aligned rows.</summary>
    public bool CanApplyAnyway { get; init; }

    /// <summary>Gets a value indicating whether a Retry is waiting for the next pass.</summary>
    public bool RetryRequested { get; init; }

    /// <summary>Gets when the record was written.</summary>
    public DateTime UpdatedUtc { get; init; }

    /// <summary>Gets when the show will be tried again, if it will.</summary>
    public DateTime? NextAttemptUtc { get; init; }
}
