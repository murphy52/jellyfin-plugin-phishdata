using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// Runs the C# aligner on envelopes the Python prior art produced for a real show and compares the
/// result with what the Python chose. Fixtures come from tools/make_alignment_fixture.py.
/// </summary>
public class GoldenParityTests
{
    private const double StartToleranceSec = 0.1;   // two frames
    private const double ConfidenceTolerance = 0.01;

    [Theory]
    [InlineData("2026-07-07")]
    public void Align_ShouldMatchPython(string date)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Alignment", date);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "show.json")));
        var video = ReadFloats(Path.Combine(dir, "video_env.f32.gz"));
        var templates = ReadFloats(Path.Combine(dir, "templates.f32.gz"));

        var tracks = new List<AlignmentTrack>();
        int offset = 0;
        foreach (var t in doc.RootElement.GetProperty("tracks").EnumerateArray())
        {
            int frames = t.GetProperty("frames").GetInt32();
            tracks.Add(new AlignmentTrack(
                t.GetProperty("title").GetString()!,
                t.GetProperty("duration_ms").GetDouble() / 1000.0,
                templates.AsSpan(offset, frames).ToArray()));
            offset += frames;
        }

        offset.Should().Be(templates.Length);

        var result = ConsensusAligner.Align(video, tracks);

        result.SkipReason.Should().BeNull();
        var expected = doc.RootElement.GetProperty("expected").EnumerateArray().ToList();
        result.Chapters.Should().HaveCount(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            var row = expected[i].EnumerateArray().ToList();
            var got = result.Chapters![i];
            got.Title.Should().Be(row[0].GetString());
            got.StartSeconds.Should().BeApproximately(row[1].GetDouble(), StartToleranceSec, "start of {0}", got.Title);
            if (row[2].ValueKind == JsonValueKind.Null)
            {
                got.Confidence.Should().BeNull();
            }
            else
            {
                got.Confidence.Should().NotBeNull();
                got.Confidence!.Value.Should().BeApproximately(row[2].GetDouble(), ConfidenceTolerance, "confidence of {0}", got.Title);
            }
        }
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
