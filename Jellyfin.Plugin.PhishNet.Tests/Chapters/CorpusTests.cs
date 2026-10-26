using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// Local-only checks over the corpus tools/make_corpus.py builds from the NAS library
/// (<c>PHISHDATA_CORPUS=&lt;corpus_dir&gt;</c>). Skipped when the corpus is absent, so CI and other
/// machines never see them. Run with <c>dotnet test --filter Category=Corpus</c>.
/// </summary>
[Trait("Category", "Corpus")]
public class CorpusTests
{
    private const double StartToleranceSec = 0.1;
    private const double ConfidenceTolerance = 0.01;

    private readonly ITestOutputHelper _output;

    public CorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [CorpusFact]
    public void Align_ShouldMatchPythonOnEveryShow()
    {
        var mismatches = new List<string>();
        int compared = 0;
        foreach (var show in CorpusShow.LoadAll())
        {
            if (show.Tracks is null)
            {
                continue;
            }

            compared++;
            var result = show.AlignLikeTheService();
            string? pythonSkip = show.PythonSkip;
            if (Normalize(result.SkipReason) != Normalize(pythonSkip))
            {
                mismatches.Add($"{show.Name}: C# {result.SkipReason ?? "aligned"}, Python {pythonSkip ?? "aligned"}");
                continue;
            }

            if (show.PythonRows is not { } rows || result.Chapters is null)
            {
                continue;
            }

            if (rows.Count != result.Chapters.Count)
            {
                mismatches.Add($"{show.Name}: {result.Chapters.Count} chapters, Python {rows.Count}");
                continue;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var (title, start, confidence) = rows[i];
                var got = result.Chapters[i];
                bool confidenceOk = confidence is null
                    ? got.Confidence is null
                    : got.Confidence is { } c && Math.Abs(c - confidence.Value) <= ConfidenceTolerance;
                if (got.Title != title || Math.Abs(got.StartSeconds - start) > StartToleranceSec || !confidenceOk)
                {
                    mismatches.Add($"{show.Name} #{i + 1} {title}: C# {got.StartSeconds:F2}s/{got.Confidence:F3}, Python {start:F2}s/{confidence:F3}");
                }
            }
        }

        _output.WriteLine($"{compared} shows compared, {mismatches.Count} mismatches");
        mismatches.ForEach(_output.WriteLine);
        compared.Should().BeGreaterThan(0);
        mismatches.Should().BeEmpty();
    }

    [CorpusFact]
    public void Align_AgreementWithEarlierChapters()
    {
        // Report, not a gate. The library's older chapters came from earlier versions of the NAS scripts
        // (constant offset, no silent-window guard), so a large gap flags a show where one of the two is wrong.
        // Set PHISHDATA_CORPUS_REPORT to also write the per-show table to a file for comparing variants.
        var report = new StringBuilder("show\tstatus\tsongs\tmatched\tmedian_abs_s\tp90_abs_s\tover_30s\tmedian_signed_s\n");
        var all = new List<double>();
        int shows = 0, aligned = 0;
        foreach (var show in CorpusShow.LoadAll().Where(s => s.Tracks is not null && s.ChaptersBy == "earlier"))
        {
            shows++;
            var result = show.AlignLikeTheService();
            if (result.Chapters is null || result.SkipReason is not null) // a low-confidence result keeps its chapters for Apply anyway
            {
                report.Append(CultureInfo.InvariantCulture, $"{show.Name}\t{result.SkipReason}\t{show.Tracks!.Count}\t\t\t\t\t\n");
                continue;
            }

            aligned++;
            var deltas = MatchByTitle(result.Chapters, show.FileChapters).ToList();
            all.AddRange(deltas.Select(Math.Abs));
            var abs = deltas.Select(Math.Abs).ToList();
            report.Append(CultureInfo.InvariantCulture, $"{show.Name}\taligned\t{result.Chapters.Count}\t{deltas.Count}\t{Quantile(abs, 0.5):F1}\t{Quantile(abs, 0.9):F1}\t{abs.Count(d => d > 30)}\t{Quantile(deltas, 0.5):F1}\n");
        }

        string summary = string.Create(CultureInfo.InvariantCulture, $"{aligned}/{shows} earlier-chaptered shows aligned; {all.Count} songs matched by title, median |Δ| {Quantile(all, 0.5):F1}s, p90 {Quantile(all, 0.9):F1}s, {all.Count(d => d > 30)} over 30s");
        _output.WriteLine(summary);
        _output.WriteLine(report.ToString());
        if (Environment.GetEnvironmentVariable("PHISHDATA_CORPUS_REPORT") is { Length: > 0 } path)
        {
            File.WriteAllText(path, summary + "\n" + report);
        }

        shows.Should().BeGreaterThan(0);
    }

    [CorpusFact]
    public void Variants_OnRealShows()
    {
        // Report: what the per-set intercept changes on real shows. "Worse" is an aligned show that is now
        // rejected, or one whose median NCC drops or whose low-confidence count rises.
        int shows = 0, changed = 0, worse = 0, better = 0;
        foreach (var show in CorpusShow.LoadAll().Where(s => s.Tracks is not null))
        {
            shows++;
            var parity = show.AlignLikeTheService(AlignmentOptions.Parity);
            var perSet = show.AlignLikeTheService(AlignmentOptions.PerSet);
            int moved = parity.Chapters is { } p && perSet.Chapters is { } q
                ? p.Zip(q).Count(x => Math.Abs(x.First.StartSeconds - x.Second.StartSeconds) > 2.0)
                : 0;
            bool same = parity.SkipReason == perSet.SkipReason && moved == 0
                && Math.Abs(parity.MedianConfidence - perSet.MedianConfidence) < 1e-9;
            if (same)
            {
                continue;
            }

            changed++;
            bool isWorse = (parity.SkipReason is null && perSet.SkipReason is not null)
                || (parity.SkipReason is null && (perSet.MedianConfidence < parity.MedianConfidence - 0.005 || perSet.LowConfidenceCount > parity.LowConfidenceCount));
            bool isBetter = !isWorse && ((parity.SkipReason is not null && perSet.SkipReason is null)
                || perSet.MedianConfidence > parity.MedianConfidence + 0.005 || perSet.LowConfidenceCount < parity.LowConfidenceCount);
            worse += isWorse ? 1 : 0;
            better += isBetter ? 1 : 0;
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(isWorse ? "WORSE " : isBetter ? "better" : "      ")} {show.Name}: parity {Describe(parity)} | per-set {Describe(perSet)} | {moved} moved >2s"));
        }

        _output.WriteLine($"{shows} shows: {changed} changed, {better} better, {worse} worse");
        shows.Should().BeGreaterThan(0);
        worse.Should().Be(0, "per-set is the default only because no real show got worse (plan §6)");
    }

    [CorpusFact]
    public void Variants_OnSetbreakInsertions()
    {
        // Real shows the parity aligner handles cleanly, with a longer setbreak spliced in before set 2.
        // The truth is known: set 1 stays put and everything after moves by the inserted length.
        var results = new Dictionary<string, (int Shows, int Aligned, int Songs, int Placed)>();
        foreach (var show in CorpusShow.LoadAll().Where(s => s.Tracks is not null))
        {
            var tracks = show.Tracks!;
            var video = show.VideoEnvelope;
            var baseline = CorpusShow.AlignLikeTheService(video, tracks, AlignmentOptions.Parity);
            int split = tracks.ToList().FindIndex(t => t.Set is { } set && set != tracks[0].Set);
            if (baseline.Chapters is not { } truth || baseline.MedianConfidence < 0.6 || baseline.LowConfidenceCount > 0 || split <= 0)
            {
                continue;
            }

            int at = (int)((truth[split].StartSeconds - 2) * AlignmentConstants.Fps);
            int gapStart = (int)((truth[split - 1].StartSeconds + tracks[split - 1].DurationSeconds) * AlignmentConstants.Fps);
            foreach (int extraSec in new[] { 300, 900, 1800 })
            {
                var spliced = Splice(video, at, gapStart, extraSec * AlignmentConstants.Fps);
                foreach (var (name, options) in new[] { ("parity", AlignmentOptions.Parity), ("per-set", AlignmentOptions.PerSet) })
                {
                    string key = $"+{extraSec / 60,2} min {name}";
                    var r = CorpusShow.AlignLikeTheService(spliced, tracks, options);
                    var acc = results.GetValueOrDefault(key);
                    acc.Shows++;
                    acc.Songs += tracks.Count;
                    if (r.Chapters is { } got && r.SkipReason is null)
                    {
                        acc.Aligned++;
                        acc.Placed += got.Select((c, i) => Math.Abs(c.StartSeconds - (truth[i].StartSeconds + (i >= split ? extraSec : 0)))).Count(d => d <= 2.0);
                    }

                    results[key] = acc;
                }
            }
        }

        foreach (var (key, r) in results.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"{key}: {r.Aligned}/{r.Shows} shows aligned, {r.Placed}/{r.Songs} songs within 2 s");
        }

        foreach (var key in results.Keys.Where(k => k.EndsWith(" parity", StringComparison.Ordinal)))
        {
            results[key.Replace(" parity", " per-set", StringComparison.Ordinal)].Placed.Should().BeGreaterThanOrEqualTo(results[key].Placed, key);
        }

        results.Should().NotBeEmpty();
    }

    [CorpusAudioFact]
    public async Task Decoder_ShouldMatchPythonEnvelope()
    {
        // The plugin decodes with Jellyfin's ffmpeg, the Python with imageio-ffmpeg's. Different builds may
        // differ in the last float bits; the envelope must not differ in any way the aligner could see.
        var encoder = new Mock<IMediaEncoder>();
        encoder.SetupGet(e => e.EncoderPath).Returns(FfmpegFactAttribute.FfmpegPath ?? "ffmpeg");
        var decoder = new EnvelopeDecoder(encoder.Object, NullLogger<EnvelopeDecoder>.Instance);
        int compared = 0;
        var problems = new List<string>();
        foreach (var show in CorpusShow.LoadAll().Where(s => s.Tracks is not null && File.Exists(s.AudioPath)))
        {
            compared++;
            var ours = await decoder.DecodeFileAsync(show.AudioPath, CancellationToken.None);
            var python = show.VideoEnvelope;
            int n = Math.Min(ours.Length, python.Length);
            double maxDiff = 0;
            for (int i = 0; i < n; i++)
            {
                maxDiff = Math.Max(maxDiff, Math.Abs(ours[i] - python[i]));
            }

            _output.WriteLine($"{show.Name}: {ours.Length} vs {python.Length} frames, max |diff| {maxDiff:E2}");
            if (Math.Abs(ours.Length - python.Length) > 1 || maxDiff > 1e-3)
            {
                problems.Add(show.Name);
            }
        }

        compared.Should().BeGreaterThan(0);
        problems.Should().BeEmpty();
    }

    /// <summary>
    /// Pairs each aligned chapter with the next existing chapter of the same title, in order, and yields
    /// aligned minus existing start. phish.in sometimes renames or splits a track after the fact, so a
    /// title has to turn up within a few chapters to count.
    /// </summary>
    private static IEnumerable<double> MatchByTitle(IReadOnlyList<AlignedChapter> aligned, IReadOnlyList<(string Title, double Start)> source)
    {
        int next = 0;
        foreach (var chapter in aligned)
        {
            string key = TitleKey(chapter.Title);
            for (int j = next; j < Math.Min(source.Count, next + 4); j++)
            {
                if (TitleKey(source[j].Title) == key)
                {
                    yield return chapter.StartSeconds - source[j].Start;
                    next = j + 1;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Inserts <paramref name="frames"/> of setbreak-like audio at <paramref name="at"/>: the show's own gap
    /// after set 1 (crowd and tuning) repeated, or quiet noise when that gap is too short to repeat.
    /// </summary>
    private static float[] Splice(float[] video, int at, int gapStart, int frames)
    {
        var filler = new float[frames];
        int gap = at - gapStart;
        var rng = new Random(at);
        for (int i = 0; i < frames; i++)
        {
            filler[i] = gap >= 10 * AlignmentConstants.Fps ? video[gapStart + (i % gap)] : 0.05f + (0.02f * (float)rng.NextDouble());
        }

        return [.. video.AsSpan(0, at), .. filler, .. video.AsSpan(at)];
    }

    private static string Describe(AlignmentResult r) => r.SkipReason ?? string.Create(CultureInfo.InvariantCulture, $"median {r.MedianConfidence:F3}, {r.LowConfidenceCount} low");

    private static string TitleKey(string title) => Regex.Replace(title.ToUpperInvariant(), "[^A-Z0-9]", string.Empty);

    private static string? Normalize(string? reason) => reason is null ? null : Regex.Replace(reason, @"\s*\(.*\)$", string.Empty);

    private static double Quantile(List<double> values, double q)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(int)Math.Round(q * (sorted.Count - 1))];
    }
}

/// <summary>A corpus test; skipped unless PHISHDATA_CORPUS points at a built corpus.</summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (CorpusShow.Root is null)
        {
            Skip = "PHISHDATA_CORPUS not set or missing (see tools/make_corpus.py)";
        }
    }
}

/// <summary>A corpus test that decodes the shows' audio; skipped unless the corpus still has its audio.mka files.</summary>
public sealed class CorpusAudioFactAttribute : FactAttribute
{
    public CorpusAudioFactAttribute()
    {
        if (CorpusShow.Root is null)
        {
            Skip = "PHISHDATA_CORPUS not set or missing (see tools/make_corpus.py)";
        }
        else if (!Directory.EnumerateDirectories(CorpusShow.Root).Any(d => File.Exists(Path.Combine(d, "audio.mka"))))
        {
            Skip = "the corpus has no audio.mka files (tools/make_corpus.py copies them again)";
        }
    }
}

/// <summary>One show of the corpus, read from the files tools/make_corpus.py writes.</summary>
internal sealed class CorpusShow
{
    private readonly string _dir;
    private readonly JsonElement _json;

    private CorpusShow(string dir)
    {
        _dir = dir;
        _json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "show.json"))).RootElement;
    }

    public static string? Root { get; } = Environment.GetEnvironmentVariable("PHISHDATA_CORPUS") is { Length: > 0 } root && Directory.Exists(root) ? root : null;

    public string Name => Path.GetFileName(_dir);

    public string AudioPath => Path.Combine(_dir, "audio.mka");

    public string? ChaptersBy => _json.GetProperty("chapters_by").GetString();

    public IReadOnlyList<(string Title, double Start)> FileChapters => _json.GetProperty("file_chapters").EnumerateArray()
        .Select(c => (c[0].GetString() ?? string.Empty, c[1].GetDouble())).ToList();

    public string? PythonSkip => _json.GetProperty("python").TryGetProperty("skip", out var s) ? s.GetString() : null;

    public IReadOnlyList<(string Title, double Start, double? Confidence)>? PythonRows =>
        _json.GetProperty("python").TryGetProperty("rows", out var rows) && PythonSkip is null
            ? rows.EnumerateArray().Select(r => (r[0].GetString()!, r[1].GetDouble(), r[2].ValueKind == JsonValueKind.Null ? (double?)null : r[2].GetDouble())).ToList()
            : null;

    public float[] VideoEnvelope => ReadFloats(Path.Combine(_dir, "video_env.f32.gz"));

    /// <summary>Gets the tracks with their templates, or null when phish.in had no usable audio.</summary>
    public IReadOnlyList<AlignmentTrack>? Tracks
    {
        get
        {
            if (!_json.TryGetProperty("tracks", out var tracks))
            {
                return null;
            }

            var templates = ReadFloats(Path.Combine(_dir, "templates.f32.gz"));
            var list = new List<AlignmentTrack>();
            int offset = 0;
            foreach (var t in tracks.EnumerateArray())
            {
                int frames = t.GetProperty("frames").GetInt32();
                var duration = t.GetProperty("duration_ms");
                list.Add(new AlignmentTrack(
                    t.GetProperty("title").GetString()!,
                    duration.ValueKind == JsonValueKind.Number ? duration.GetDouble() / 1000.0 : 0,
                    frames == 0 ? null : templates.AsSpan(offset, frames).ToArray(),
                    t.GetProperty("set").GetString()));
                offset += frames;
            }

            return list;
        }
    }

    public static IEnumerable<CorpusShow> LoadAll() => Root is null
        ? Enumerable.Empty<CorpusShow>()
        : Directory.GetDirectories(Root).Where(d => File.Exists(Path.Combine(d, "show.json"))).OrderBy(d => d, StringComparer.Ordinal).Select(d => new CorpusShow(d));

    /// <summary>Applies the gates in the order ChapterMarkerService does, then aligns.</summary>
    public AlignmentResult AlignLikeTheService(AlignmentOptions? options = null) => AlignLikeTheService(VideoEnvelope, Tracks!, options);

    public static AlignmentResult AlignLikeTheService(float[] video, IReadOnlyList<AlignmentTrack> tracks, AlignmentOptions? options = null)
    {
        double videoSeconds = video.Length / (double)AlignmentConstants.Fps;
        double phishInSeconds = tracks.Sum(t => t.DurationSeconds);
        if ((AlignmentGates.CheckVideoLength(videoSeconds) ?? AlignmentGates.CheckDuration(videoSeconds, phishInSeconds)) is { } gate)
        {
            return new AlignmentResult(null, 0, 0, gate);
        }

        var result = ConsensusAligner.Align(video, tracks, options);
        if (result.SkipReason is not null || result.Chapters is not { } chapters)
        {
            return result;
        }

        // The set gate came with per-set intercepts, so parity runs stay comparable with the Python.
        var rejected = AlignmentGates.CheckMedian(result.MedianConfidence)
            ?? (options?.PerSetIntercept == true ? AlignmentGates.CheckSets(tracks, chapters) : null);
        return rejected is null ? result : result with { SkipReason = rejected };
    }

    private static float[] ReadFloats(string path)
    {
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        var bytes = ms.ToArray();
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
        return floats;
    }
}
