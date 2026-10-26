using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Jellyfin.Plugin.PhishNet.Chapters.Alignment.AlignmentConstants;

namespace Jellyfin.Plugin.PhishNet.Chapters.Alignment;

/// <summary>
/// The show-level guards from phish_core.process_show and phish-fleet.py. Each returns a reason
/// (worded as the NAS fleet log words it) when the show should not be chaptered, otherwise null.
/// </summary>
public static class AlignmentGates
{
    /// <summary>
    /// Rejects videos too short to be a show.
    /// </summary>
    /// <param name="videoSeconds">Length of the decoded video audio.</param>
    /// <returns>A reason, or null.</returns>
    public static string? CheckVideoLength(double videoSeconds)
    {
        if (videoSeconds < 1)
        {
            return "no video audio decoded";
        }

        return videoSeconds < MinVideoSeconds ? $"too-short ({Hms(videoSeconds)})" : null;
    }

    /// <summary>
    /// Rejects clips and partial videos whose length is far from the phish.in show's.
    /// </summary>
    /// <param name="videoSeconds">Length of the decoded video audio.</param>
    /// <param name="phishInSeconds">Sum of phish.in track durations; 0 when unknown.</param>
    /// <returns>A reason, or null.</returns>
    public static string? CheckDuration(double videoSeconds, double phishInSeconds)
    {
        if (phishInSeconds <= 0)
        {
            return null;
        }

        double ratio = videoSeconds / phishInSeconds;
        return ratio is >= DurationRatioLow and <= DurationRatioHigh
            ? null
            : $"duration-mismatch video={Hms(videoSeconds)} phishin={Hms(phishInSeconds)}";
    }

    /// <summary>
    /// Rejects alignments whose median confidence is too low to trust.
    /// </summary>
    /// <param name="median">Median refine NCC.</param>
    /// <returns>A reason, or null.</returns>
    public static string? CheckMedian(double median)
    {
        return median < MinMedianConfidence
            ? string.Format(CultureInfo.InvariantCulture, "low-confidence median={0:0.00}", median)
            : null;
    }

    /// <summary>
    /// Rejects a show with a set that nothing in the video confirmed. Its chapters would be fitted times
    /// only, and a setbreak or encore break the fit can't see would make them all early or late.
    /// </summary>
    /// <param name="tracks">The tracks that were aligned, in show order.</param>
    /// <param name="chapters">The chapters, one per track.</param>
    /// <param name="minTemplateFrames">Shortest template that may confirm a set (<see cref="AlignmentOptions.MinAnchorTemplateSeconds"/>).</param>
    /// <returns>A reason, or null.</returns>
    public static string? CheckSets(IReadOnlyList<AlignmentTrack> tracks, IReadOnlyList<AlignedChapter> chapters, int minTemplateFrames = 0)
    {
        var set = ConsensusAligner.SetIndex(tracks);
        if (set.Length == 0 || set[^1] == 0)
        {
            return null;
        }

        for (int s = 0; s <= set[^1]; s++)
        {
            var members = Enumerable.Range(0, set.Length).Where(i => set[i] == s).ToList();
            if (!members.Any(i => chapters[i].Confidence >= LowConfidence && (tracks[i].Template?.Length ?? 0) >= minTemplateFrames))
            {
                return $"unconfirmed-set {tracks[members[0]].Set ?? "?"}";
            }
        }

        return null;
    }

    /// <summary>
    /// Formats seconds as H:MM:SS.
    /// </summary>
    /// <param name="seconds">Seconds.</param>
    /// <returns>The formatted time.</returns>
    public static string Hms(double seconds)
    {
        long s = (long)Math.Round(seconds);
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", s / 3600, s % 3600 / 60, s % 60);
    }
}

/// <summary>
/// Median with numpy's convention (mean of the two middle values for an even count).
/// </summary>
internal static class Stats
{
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("Median of an empty sequence.", nameof(values));
        }

        return MedianInPlace(sorted);
    }

    // Sorts the values it is given, so callers pass a copy they don't need in order.
    public static double MedianInPlace(Span<double> values)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Median of an empty sequence.", nameof(values));
        }

        values.Sort();
        int m = values.Length / 2;
        return values.Length % 2 == 1 ? values[m] : (values[m - 1] + values[m]) / 2.0;
    }
}
