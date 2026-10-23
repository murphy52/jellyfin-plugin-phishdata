using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>What an item's chapters in Jellyfin's database are.</summary>
public enum ChapterSituation
{
    /// <summary>No chapters at all.</summary>
    Missing,

    /// <summary>Exactly the dummy set Jellyfin generates for the current server setting.</summary>
    Dummy,

    /// <summary>Exactly the chapters this plugin saved.</summary>
    Ours,

    /// <summary>Real chapters from the file, another tool or a person. Never replaced.</summary>
    Other,
}

/// <summary>
/// What Jellyfin's dummy chapters look like for one item under the current server setting
/// (FFProbeVideoInfo.CreateDummyChapters, then NormalizeChapterNames).
/// </summary>
/// <param name="RuntimeTicks">The item's runtime.</param>
/// <param name="DurationSeconds">The server's <c>DummyChapterDuration</c>.</param>
/// <param name="NameFormat">The localized <c>ChapterNameValue</c>, e.g. "Chapter {0}".</param>
public sealed record DummySignature(long RuntimeTicks, int DurationSeconds, string NameFormat);

/// <summary>
/// Decides whether an item's current chapters may be replaced.
/// </summary>
public static class ChapterDetector
{
    /// <summary>
    /// Classifies an item's chapters.
    /// </summary>
    /// <param name="current">The chapters Jellyfin has now.</param>
    /// <param name="ours">The chapters this plugin last saved for the item, if any.</param>
    /// <param name="dummies">Jellyfin's dummy signature, or null when dummies aren't to be replaced or the server makes none.</param>
    /// <returns>The situation.</returns>
    public static ChapterSituation Classify(IReadOnlyList<ChapterInfo> current, IReadOnlyList<AlignedChapter>? ours, DummySignature? dummies)
    {
        if (current.Count == 0)
        {
            return ChapterSituation.Missing;
        }

        if (ours is not null && Matches(current, ours))
        {
            return ChapterSituation.Ours;
        }

        return dummies is not null && IsDummySet(current, dummies) ? ChapterSituation.Dummy : ChapterSituation.Other;
    }

    /// <summary>
    /// Converts chapter rows to what <c>SaveChapters</c> takes.
    /// </summary>
    /// <param name="chapters">The rows.</param>
    /// <returns>Jellyfin chapters.</returns>
    public static List<ChapterInfo> ToChapterInfos(IEnumerable<AlignedChapter> chapters) =>
        chapters.Select(c => new ChapterInfo { StartPositionTicks = ToTicks(c.StartSeconds), Name = c.Title }).ToList();

    /// <summary>
    /// Converts seconds to ticks the way chapters are saved, so saved and cached rows compare exactly.
    /// </summary>
    /// <param name="seconds">Seconds.</param>
    /// <returns>Ticks.</returns>
    public static long ToTicks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);

    private static bool Matches(IReadOnlyList<ChapterInfo> current, IReadOnlyList<AlignedChapter> ours)
    {
        if (current.Count != ours.Count)
        {
            return false;
        }

        for (int i = 0; i < ours.Count; i++)
        {
            if (current[i].StartPositionTicks != ToTicks(ours[i].StartSeconds) || current[i].Name != ours[i].Title)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDummySet(IReadOnlyList<ChapterInfo> current, DummySignature dummies)
    {
        if (dummies.DurationSeconds <= 0 || dummies.RuntimeTicks <= 0)
        {
            return false;
        }

        long step = TimeSpan.FromSeconds(dummies.DurationSeconds).Ticks;
        int expected = Math.Max(1, (int)(dummies.RuntimeTicks / step));
        if (current.Count != expected)
        {
            return false;
        }

        for (int i = 0; i < current.Count; i++)
        {
            var name = string.Format(CultureInfo.InvariantCulture, dummies.NameFormat, (i + 1).ToString(CultureInfo.InvariantCulture));
            if (current[i].StartPositionTicks != i * step || current[i].Name != name)
            {
                return false;
            }
        }

        return true;
    }
}
