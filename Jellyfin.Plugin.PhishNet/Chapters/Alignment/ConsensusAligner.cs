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
/// that keeps it puts set 2 later than a single line predicts by the setbreak's length. Breaks only add
/// time, so a set that borrows the previous set's intercept, and a song that scores low in the usual
/// window, are also searched further forward.
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
        var templates = tracks.Select(t => t.Template is null ? null : t.Template.Length > TemplateFrames ? t.Template[..TemplateFrames] : t.Template).ToList();

        // NaN and infinity compare false against every threshold, so they would hide the cause (or worse,
        // a NaN median passes the median gate). Name it instead.
        if (!AllFinite(video))
        {
            return Rejected("non-finite video");
        }

        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i] is { } t && !AllFinite(t))
            {
                return Rejected($"non-finite template {i + 1}/{tracks.Count}");
            }
        }

        var searcher = new NccSearcher(video, TemplateFrames);

        var cumulativeStart = new double[tracks.Count];
        double cumulative = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            cumulativeStart[i] = cumulative;
            cumulative += tracks[i].DurationSeconds;
        }

        // Sets are runs of consecutive tracks with the same set name, so a name that comes back later is a
        // new set; without per-set intercepts the whole show is one set.
        var setIndex = SetIndex(tracks, perSet);
        int setCount = tracks.Count > 0 ? setIndex[^1] + 1 : 1;

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

        // A set without MinSetAnchors anchors that agree with each other (often a one- or two-song encore), or
        // whose fit would start it before the set ahead of it, borrows the intercept of the set before it; the
        // first set falls back to the whole-show fit.
        var intercepts = new double[setCount];
        var borrowed = new bool[setCount];
        var ownGuess = new double?[setCount];
        for (int s = 0; s < setCount; s++)
        {
            var own = anchors.Where(p => p.Set == s).Select(p => p.Y - (b * p.X)).ToList();
            if (setCount == 1)
            {
                intercepts[s] = a;
            }
            else if (Consistent(own) is { } fitted && (s == 0 || fitted >= intercepts[s - 1] - AgreementSeconds))
            {
                intercepts[s] = fitted;
            }
            else
            {
                intercepts[s] = s > 0 ? intercepts[s - 1] : a;
                borrowed[s] = s > 0;

                // A lone anchor isn't trusted for the intercept, but where it says the set starts is worth a
                // look if the borrowed time finds nothing (an encore after a break longer than the reach).
                var later = s > 0 ? own.Where(r => r >= intercepts[s - 1] - AgreementSeconds).ToList() : [];
                ownGuess[s] = later.Count > 0 ? Stats.Median(later) : null;
            }
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
            bool reachForward = borrowed[setIndex[i]];
            int hi = (int)((predicted + RefineWindowSeconds + (reachForward ? ForwardReachSeconds : 0)) * Fps);
            var correlation = searcher.Correlate(template);
            var (index, score) = correlation.Search(lo, hi);
            if (reachForward && score < LowConfidence && ownGuess[setIndex[i]] is { } guess)
            {
                double own = guess + (b * cumulativeStart[i]);
                var near = correlation.Search(Math.Max(lo, (int)((own - RefineWindowSeconds) * Fps)), (int)((own + RefineWindowSeconds) * Fps));
                if (near.Score > score)
                {
                    (index, score) = near;
                }
            }
            else if (perSet && !reachForward && score < LowConfidence)
            {
                // A pause inside the set (a stop to fix gear, a long story) puts the rest of it later than the line.
                var wider = correlation.Search(lo, hi + (ForwardReachSeconds * Fps));
                if (wider.Score > score)
                {
                    (index, score) = wider;
                }
            }

            double start = score >= RefineAcceptConfidence ? index / (double)Fps : predicted;
            chapters.Add(new AlignedChapter(tracks[i].Title, start, score));
            previous = (int)(start * Fps);
        }

        var scores = chapters.Where(c => c.Confidence.HasValue).Select(c => c.Confidence!.Value).ToList();
        double median = scores.Count > 0 ? Stats.Median(scores) : 0.0;
        int low = scores.Count(s => s < LowConfidence);
        return new AlignmentResult(chapters, median, low, null);
    }

    // The median of the largest group of intercepts that agree with one another, or null when fewer than
    // MinSetAnchors agree: two anchors a setbreak apart average to a time that fits neither.
    private static double? Consistent(List<double> intercepts)
    {
        var best = intercepts.Select(x => intercepts.Where(y => Math.Abs(y - x) <= AgreementSeconds).ToList())
            .OrderByDescending(g => g.Count).FirstOrDefault();
        return best is { Count: >= MinSetAnchors } ? Stats.Median(best) : null;
    }

    /// <summary>
    /// Numbers each track's set: runs of consecutive tracks with the same set name, in show order.
    /// </summary>
    /// <param name="tracks">Tracks in show order.</param>
    /// <param name="perSet">False to put the whole show in set 0.</param>
    /// <returns>The set number of each track.</returns>
    public static int[] SetIndex(IReadOnlyList<AlignmentTrack> tracks, bool perSet = true)
    {
        var index = new int[tracks.Count];
        for (int i = 1; i < tracks.Count; i++)
        {
            index[i] = index[i - 1] + (perSet && tracks[i].Set != tracks[i - 1].Set ? 1 : 0);
        }

        return index;
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

    private static bool AllFinite(float[] values)
    {
        foreach (var v in values)
        {
            if (!float.IsFinite(v))
            {
                return false;
            }
        }

        return true;
    }

    private static AlignmentResult Rejected(string reason) => new(null, 0.0, 0, reason);
}
