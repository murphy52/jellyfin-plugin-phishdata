using System;
using System.Collections.Generic;
using System.Linq;
using static Jellyfin.Plugin.PhishNet.Chapters.Alignment.AlignmentConstants;

namespace Jellyfin.Plugin.PhishNet.Chapters.Alignment;

/// <summary>
/// One track of the show as phish.in describes it.
/// </summary>
/// <param name="Title">Song title, used as the chapter name.</param>
/// <param name="DurationSeconds">Track length from phish.in; 0 when unknown.</param>
/// <param name="Template">Envelope of the track's opening seconds, or null when it has no audio.</param>
public sealed record AlignmentTrack(string Title, double DurationSeconds, float[]? Template);

/// <summary>
/// A song's place in the video.
/// </summary>
/// <param name="Title">Song title.</param>
/// <param name="StartSeconds">Start position in the video, in seconds.</param>
/// <param name="Confidence">Refine NCC, or null when the track had no audio and the fitted time was used.</param>
public sealed record AlignedChapter(string Title, double StartSeconds, double? Confidence);

/// <summary>
/// Outcome of aligning a show.
/// </summary>
/// <param name="Chapters">One chapter per track, or null when the alignment was rejected.</param>
/// <param name="MedianConfidence">Median refine NCC over tracks with audio.</param>
/// <param name="LowConfidenceCount">Tracks whose refine NCC is below <see cref="AlignmentConstants.LowConfidence"/>.</param>
/// <param name="SkipReason">Why the alignment was rejected, or null.</param>
public sealed record AlignmentResult(IReadOnlyList<AlignedChapter>? Chapters, double MedianConfidence, int LowConfidenceCount, string? SkipReason);

/// <summary>
/// Consensus-offset alignment, ported from phish_core.align:
/// match each track independently, fit position = a + b * cumulative_time over the confident
/// matches (b captures drift), require a quorum to agree with the line, then refine each song in a
/// window around its fitted time.
/// </summary>
public static class ConsensusAligner
{
    /// <summary>
    /// Aligns the tracks against the video's envelope.
    /// </summary>
    /// <param name="video">The video's loudness envelope.</param>
    /// <param name="tracks">Tracks in show order, soundchecks already removed.</param>
    /// <returns>The chapters, or a skip reason.</returns>
    public static AlignmentResult Align(float[] video, IReadOnlyList<AlignmentTrack> tracks)
    {
        const int minGapFrames = MinGapSeconds * Fps;
        var searcher = new NccSearcher(video, TemplateFrames);
        var templates = tracks.Select(t => t.Template is null ? null : t.Template.Length > TemplateFrames ? t.Template[..TemplateFrames] : t.Template).ToList();

        var cumulativeStart = new double[tracks.Count];
        double cumulative = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            cumulativeStart[i] = cumulative;
            cumulative += tracks[i].DurationSeconds;
        }

        // Stage 1: an independent global match per track.
        var anchors = new List<(double X, double Y)>();
        for (int i = 0; i < tracks.Count; i++)
        {
            if (templates[i] is { } template)
            {
                var (index, score) = searcher.Correlate(template).Search(0, null);
                if (score >= AnchorConfidence)
                {
                    anchors.Add((cumulativeStart[i], index / (double)Fps));
                }
            }
        }

        // Stage 2: robust line over the anchors (Theil-Sen slope clamped to a sane drift).
        int need = Math.Max(3, tracks.Count / 4);
        if (anchors.Count < need)
        {
            return Rejected($"few-anchors {anchors.Count}/{tracks.Count}");
        }

        var slopes = new List<double>();
        for (int i = 0; i < anchors.Count; i++)
        {
            for (int j = i + 1; j < anchors.Count; j++)
            {
                double dx = anchors[j].X - anchors[i].X;
                if (Math.Abs(dx) > 1)
                {
                    slopes.Add((anchors[j].Y - anchors[i].Y) / dx);
                }
            }
        }

        double b = slopes.Count > 0 ? Math.Clamp(Stats.Median(slopes), SlopeMin, SlopeMax) : 1.0;
        double a = Stats.Median(anchors.Select(p => p.Y - (b * p.X)));
        int agree = anchors.Count(p => Math.Abs(p.Y - (a + (b * p.X))) <= AgreementSeconds);
        if (agree < need)
        {
            return Rejected($"no-consensus agree={agree}/{anchors.Count}");
        }

        // Stage 3: place each song via the line and refine it in a window around that time.
        var chapters = new List<AlignedChapter>(tracks.Count);
        int previous = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            double predicted = Math.Max(0.0, a + (b * cumulativeStart[i]));
            if (templates[i] is not { } template)
            {
                chapters.Add(new AlignedChapter(tracks[i].Title, predicted, null));
                previous = (int)(predicted * Fps);
                continue;
            }

            int lo = Math.Max(Math.Max(previous + (i == 0 ? 0 : minGapFrames), (int)((predicted - RefineWindowSeconds) * Fps)), 0);
            int hi = (int)((predicted + RefineWindowSeconds) * Fps);
            var (index, score) = searcher.Correlate(template).Search(lo, hi);
            double start = score >= RefineAcceptConfidence ? index / (double)Fps : predicted;
            chapters.Add(new AlignedChapter(tracks[i].Title, start, score));
            previous = (int)(start * Fps);
        }

        var scores = chapters.Where(c => c.Confidence.HasValue).Select(c => c.Confidence!.Value).ToList();
        double median = scores.Count > 0 ? Stats.Median(scores) : 0.0;
        int low = scores.Count(s => s < LowConfidence);
        return new AlignmentResult(chapters, median, low, null);
    }

    private static AlignmentResult Rejected(string reason) => new(null, 0.0, 0, reason);
}
