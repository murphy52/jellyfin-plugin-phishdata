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
/// <param name="Set">phish.in set name ("Set 1", "Encore"), or null when unknown.</param>
public sealed record AlignmentTrack(string Title, double DurationSeconds, float[]? Template, string? Set = null);

/// <summary>
/// Variations on the alignment, judged on the corpus before they become the default.
/// </summary>
/// <param name="PerSetIntercept">
/// Fit one intercept per set, sharing the slope. phish.in's durations leave out the setbreak, so a webcast
/// that keeps it puts set 2 later than a single line predicts by the setbreak's length.
/// </param>
public sealed record AlignmentOptions(bool PerSetIntercept)
{
    /// <summary>Gets the options that match phish_core.align exactly (golden parity).</summary>
    public static AlignmentOptions Parity { get; } = new(false);

    /// <summary>Gets the options with a per-set intercept.</summary>
    public static AlignmentOptions PerSet { get; } = new(true);
}

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
    /// <param name="options">Variations on the alignment; <see cref="AlignmentOptions.Parity"/> when null.</param>
    /// <returns>The chapters, or a skip reason.</returns>
    public static AlignmentResult Align(float[] video, IReadOnlyList<AlignmentTrack> tracks, AlignmentOptions? options = null)
    {
        bool perSet = options?.PerSetIntercept ?? false;
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

        // Sets in show order; without per-set intercepts the whole show is one set.
        var setIndex = new int[tracks.Count];
        var setNames = new List<string?>();
        for (int i = 0; i < tracks.Count; i++)
        {
            string? name = perSet ? tracks[i].Set : null;
            int found = setNames.IndexOf(name);
            setIndex[i] = found >= 0 ? found : setNames.Count;
            if (found < 0)
            {
                setNames.Add(name);
            }
        }

        // Stage 1: an independent global match per track.
        var anchors = new List<(double X, double Y, int Set)>();
        for (int i = 0; i < tracks.Count; i++)
        {
            if (templates[i] is { } template)
            {
                var (index, score) = searcher.Correlate(template).Search(0, null);
                if (score >= AnchorConfidence)
                {
                    anchors.Add((cumulativeStart[i], index / (double)Fps, setIndex[i]));
                }
            }
        }

        // Stage 2: robust line over the anchors (Theil-Sen slope clamped to a sane drift).
        int need = Math.Max(3, tracks.Count / 4);
        if (anchors.Count < need)
        {
            return Rejected($"few-anchors {anchors.Count}/{tracks.Count}");
        }

        // Pairs across a setbreak would fold the step into the slope, so with per-set intercepts only
        // pairs within a set count, unless no set has two anchors.
        var slopes = Slopes(anchors, withinSets: true);
        if (slopes.Count == 0)
        {
            slopes = Slopes(anchors, withinSets: false);
        }

        double b = slopes.Count > 0 ? Math.Clamp(Stats.Median(slopes), SlopeMin, SlopeMax) : 1.0;
        double a = Stats.Median(anchors.Select(p => p.Y - (b * p.X)));

        // A set with too few anchors of its own (often a one- or two-song encore) borrows the intercept of
        // the set before it, since encore breaks are short; the first set falls back to the whole-show fit.
        var intercepts = new double[setNames.Count];
        for (int s = 0; s < setNames.Count; s++)
        {
            var own = anchors.Where(p => p.Set == s).Select(p => p.Y - (b * p.X)).ToList();
            intercepts[s] = own.Count >= MinSetAnchors || setNames.Count == 1 ? (own.Count > 0 ? Stats.Median(own) : a)
                : s > 0 ? intercepts[s - 1] : a;
        }

        int agree = anchors.Count(p => Math.Abs(p.Y - (intercepts[p.Set] + (b * p.X))) <= AgreementSeconds);
        if (agree < need)
        {
            return Rejected($"no-consensus agree={agree}/{anchors.Count}");
        }

        // Stage 3: place each song via the line and refine it in a window around that time.
        var chapters = new List<AlignedChapter>(tracks.Count);
        int previous = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            double predicted = Math.Max(0.0, intercepts[setIndex[i]] + (b * cumulativeStart[i]));
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

    private static List<double> Slopes(List<(double X, double Y, int Set)> anchors, bool withinSets)
    {
        var slopes = new List<double>();
        for (int i = 0; i < anchors.Count; i++)
        {
            for (int j = i + 1; j < anchors.Count; j++)
            {
                double dx = anchors[j].X - anchors[i].X;
                if (Math.Abs(dx) > 1 && (!withinSets || anchors[i].Set == anchors[j].Set))
                {
                    slopes.Add((anchors[j].Y - anchors[i].Y) / dx);
                }
            }
        }

        return slopes;
    }

    private static AlignmentResult Rejected(string reason) => new(null, 0.0, 0, reason);
}
