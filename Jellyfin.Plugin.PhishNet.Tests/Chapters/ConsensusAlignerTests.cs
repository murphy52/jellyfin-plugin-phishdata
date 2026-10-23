using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// Synthetic shows: each song is a random loudness curve, the "video" is the songs laid out with a
/// lead-in, drift (extra time after each song) and optionally a setbreak gap, and each template is the song's first 90 seconds
/// with a little noise, as a soundboard MP3 would be against a webcast.
/// </summary>
public class ConsensusAlignerTests
{
    private const int Fps = AlignmentConstants.Fps;

    [Fact]
    public void Align_ShouldPlaceEverySong_WithDrift()
    {
        var show = SyntheticShow.Build(seed: 10, songs: 12, leadInSec: 60, drift: 1.02, setbreakAfter: null, setbreakSec: 0);

        var result = ConsensusAligner.Align(show.Video, show.Tracks);

        result.SkipReason.Should().BeNull();
        result.Chapters.Should().HaveCount(12);
        for (int i = 0; i < 12; i++)
        {
            result.Chapters![i].StartSeconds.Should().BeApproximately(show.TrueStarts[i], 2.0, "song {0}", i);
            result.Chapters[i].Title.Should().Be($"Song {i + 1}");
        }

        result.MedianConfidence.Should().BeGreaterThan(0.8);
        result.LowConfidenceCount.Should().Be(0);
    }

    [Fact]
    public void Align_ShouldSurviveSetbreakStep()
    {
        var show = SyntheticShow.Build(seed: 11, songs: 14, leadInSec: 64, drift: 1.0, setbreakAfter: 7, setbreakSec: 100);

        var result = ConsensusAligner.Align(show.Video, show.Tracks);

        result.SkipReason.Should().BeNull();
        for (int i = 0; i < 14; i++)
        {
            result.Chapters![i].StartSeconds.Should().BeApproximately(show.TrueStarts[i], 2.0, "song {0}", i);
        }
    }

    [Fact]
    public void Align_ShouldUseFittedTime_ForTrackWithoutAudio()
    {
        var show = SyntheticShow.Build(seed: 12, songs: 10, leadInSec: 30, drift: 1.0, setbreakAfter: null, setbreakSec: 0);
        var tracks = show.Tracks.Select((t, i) => i == 4 ? t with { Template = null } : t).ToList();

        var result = ConsensusAligner.Align(show.Video, tracks);

        result.SkipReason.Should().BeNull();
        result.Chapters![4].Confidence.Should().BeNull();
        result.Chapters[4].StartSeconds.Should().BeApproximately(show.TrueStarts[4], 5.0);
    }

    [Fact]
    public void Align_ShouldSkip_WhenTemplatesMatchNothing()
    {
        var show = SyntheticShow.Build(seed: 13, songs: 8, leadInSec: 30, drift: 1.0, setbreakAfter: null, setbreakSec: 0);
        var rng = new Random(99);
        var tracks = show.Tracks.Select(t => t with { Template = NccTests.RandomEnvelope(rng, 90 * Fps) }).ToList();

        var result = ConsensusAligner.Align(show.Video, tracks);

        result.Chapters.Should().BeNull();
        result.SkipReason.Should().StartWith("few-anchors ").And.EndWith("/8");
    }

    [Fact]
    public void Align_ShouldSkip_WhenConfidentMatchesDisagree()
    {
        // Songs appear in the video in reverse order: every match is confident but no line fits them.
        var show = SyntheticShow.Build(seed: 14, songs: 8, leadInSec: 30, drift: 1.0, setbreakAfter: null, setbreakSec: 0);
        var reversed = show.Tracks.Reverse().ToList();

        var result = ConsensusAligner.Align(show.Video, reversed);

        result.Chapters.Should().BeNull();
        result.SkipReason.Should().StartWith("no-consensus agree=");
    }

    [Theory]
    [InlineData(3600, 3600, null)]
    [InlineData(3600, 6100, "duration-mismatch video=1:00:00 phishin=1:41:40")]
    [InlineData(3600, 2300, "duration-mismatch video=1:00:00 phishin=0:38:20")]
    [InlineData(3600, 0, null)]
    public void CheckDuration_ShouldGateClipsAndPartials(double videoSec, double phishInSec, string? expected)
    {
        AlignmentGates.CheckDuration(videoSec, phishInSec).Should().Be(expected);
    }

    [Theory]
    [InlineData(599.0, "too-short (0:09:59)")]
    [InlineData(0.5, "no video audio decoded")]
    [InlineData(600.0, null)]
    public void CheckVideoLength_ShouldRejectShortVideos(double videoSec, string? expected)
    {
        AlignmentGates.CheckVideoLength(videoSec).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.39, "low-confidence median=0.39")]
    [InlineData(0.40, null)]
    public void CheckMedian_ShouldGateWeakAlignments(double median, string? expected)
    {
        AlignmentGates.CheckMedian(median).Should().Be(expected);
    }

    internal sealed record SyntheticShow(float[] Video, IReadOnlyList<AlignmentTrack> Tracks, double[] TrueStarts)
    {
        public static SyntheticShow Build(int seed, int songs, double leadInSec, double drift, int? setbreakAfter, double setbreakSec)
        {
            var rng = new Random(seed);
            var video = new List<float>(NccTests.RandomEnvelope(rng, (int)(leadInSec * Fps)).Select(x => x * 0.3f));
            var tracks = new List<AlignmentTrack>();
            var starts = new List<double>();
            for (int i = 0; i < songs; i++)
            {
                if (setbreakAfter == i)
                {
                    video.AddRange(NccTests.RandomEnvelope(rng, (int)(setbreakSec * Fps)).Select(x => x * 0.3f));
                }

                double durSec = 240 + rng.Next(0, 600);
                var song = NccTests.RandomEnvelope(rng, (int)(durSec * Fps));

                // Webcast drift is extra time between songs (crowd, tuning), not slower audio, so it
                // shows up as gaps that grow with each song's length.
                starts.Add(video.Count / (double)Fps);
                video.AddRange(song.Select(x => x + (float)(0.05 * rng.NextDouble())));
                video.AddRange(NccTests.RandomEnvelope(rng, (int)((drift - 1.0) * durSec * Fps)).Select(x => x * 0.3f));

                var template = song.Take(90 * Fps).Select(x => x + (float)(0.05 * rng.NextDouble())).ToArray();
                tracks.Add(new AlignmentTrack($"Song {i + 1}", durSec, template));
            }

            video.AddRange(NccTests.RandomEnvelope(rng, 120 * Fps).Select(x => x * 0.3f));
            return new SyntheticShow(video.ToArray(), tracks, starts.ToArray());
        }
    }
}
