using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>
/// Structural checks on every chapter list before it goes to <c>IChapterManager.SaveChapters</c>, which
/// doesn't sort or validate rows. Apply anyway may skip the confidence gates, never these.
/// </summary>
public static class ChapterValidator
{
    /// <summary>Most chapters a list may have.</summary>
    public const int MaxChapters = 100;

    /// <summary>Longest chapter title.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Checks a chapter list against the item's runtime.
    /// </summary>
    /// <param name="chapters">The chapters, in order.</param>
    /// <param name="runtimeTicks">The item's runtime.</param>
    /// <returns>Why the list can't be saved, or null when it can.</returns>
    public static string? Validate(IReadOnlyList<AlignedChapter> chapters, long runtimeTicks)
    {
        if (runtimeTicks <= 0)
        {
            return "the item has no runtime yet";
        }

        if (chapters.Count == 0)
        {
            return "no chapters";
        }

        if (chapters.Count > MaxChapters)
        {
            return $"{chapters.Count} chapters, more than {MaxChapters}";
        }

        long previous = -1;
        for (int i = 0; i < chapters.Count; i++)
        {
            var c = chapters[i];
            var label = $"chapter {i + 1} '{c.Title}'";
            if (string.IsNullOrWhiteSpace(c.Title))
            {
                return $"chapter {i + 1} has no title";
            }

            if (c.Title.Length > MaxTitleLength)
            {
                return $"chapter {i + 1} has a title longer than {MaxTitleLength} characters";
            }

            if (!double.IsFinite(c.StartSeconds))
            {
                return $"{label} start is not a number";
            }

            if (c.StartSeconds < 0)
            {
                return $"{label} starts before the start of the video";
            }

            long ticks = ChapterDetector.ToTicks(c.StartSeconds);
            if (ticks >= runtimeTicks)
            {
                return $"{label} starts at {AlignmentGates.Hms(c.StartSeconds)}, past the end of the video";
            }

            if (ticks <= previous)
            {
                return $"{label} starts at {AlignmentGates.Hms(c.StartSeconds)}, not after the chapter before it";
            }

            previous = ticks;
        }

        return null;
    }
}
