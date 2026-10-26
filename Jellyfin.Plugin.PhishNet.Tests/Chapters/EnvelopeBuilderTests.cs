using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The envelope is built while ffmpeg's PCM streams in, so memory stays O(frames) for a 3-hour show.
/// </summary>
public class EnvelopeBuilderTests
{
    private const int Hop = AlignmentConstants.SampleRate / AlignmentConstants.Fps;

    [Fact]
    public void Frame_ShouldBeLogRmsOfItsHop()
    {
        var builder = new EnvelopeBuilder();
        builder.Add(Enumerable.Repeat(0.25f, Hop).ToArray());

        var env = builder.ToArray();

        env.Should().HaveCount(1);
        env[0].Should().BeApproximately((float)Math.Log(1 + (Math.Sqrt((0.25 * 0.25) + 1e-9) * 20.0)), 1e-6f);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonFiniteSample_ShouldCountAsSilence(float bad)
    {
        var samples = Enumerable.Repeat(0.25f, Hop).ToArray();
        var expected = new EnvelopeBuilder();
        samples[7] = 0f;
        expected.Add(samples);

        var builder = new EnvelopeBuilder();
        samples[7] = bad;
        builder.Add(samples);

        builder.ToArray().Should().Equal(expected.ToArray());
    }

    [Fact]
    public void PartialLastHop_ShouldBeDropped()
    {
        var builder = new EnvelopeBuilder();
        builder.Add(new float[(Hop * 3) + 17]);

        builder.ToArray().Should().HaveCount(3);
        builder.FrameCount.Should().Be(3);
    }

    [Fact]
    public void ChunkedInput_ShouldEqualOneShotInput()
    {
        var rng = new Random(9);
        var samples = Enumerable.Range(0, (Hop * 10) + 5).Select(_ => (float)(rng.NextDouble() - 0.5)).ToArray();

        var once = new EnvelopeBuilder();
        once.Add(samples);

        var chunked = new EnvelopeBuilder();
        int pos = 0;
        foreach (var size in new[] { 1, 399, 401, 7, 1000, 13 })
        {
            chunked.Add(samples.AsSpan(pos, size));
            pos += size;
        }

        chunked.Add(samples.AsSpan(pos));

        chunked.ToArray().Should().Equal(once.ToArray());
    }

    [Fact]
    public void AddBytes_ShouldDecodeLittleEndianFloats()
    {
        var samples = Enumerable.Repeat(0.5f, Hop).ToArray();
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var fromFloats = new EnvelopeBuilder();
        fromFloats.Add(samples);
        var fromBytes = new EnvelopeBuilder();
        fromBytes.AddBytes(bytes.AsSpan(0, 3));
        fromBytes.AddBytes(bytes.AsSpan(3));

        fromBytes.ToArray().Should().Equal(fromFloats.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void AddBytes_ShouldKeepEveryByte_WhenReadsSplitSamplesUnevenly(int readSize)
    {
        // A pipe read can return any number of bytes, so a sample can arrive over several reads.
        var rng = new Random(readSize);
        var samples = Enumerable.Range(0, Hop * 3).Select(_ => (float)((rng.NextDouble() * 2) - 1)).ToArray();
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var fromFloats = new EnvelopeBuilder();
        fromFloats.Add(samples);
        var fromBytes = new EnvelopeBuilder();
        for (int pos = 0; pos < bytes.Length; pos += readSize)
        {
            fromBytes.AddBytes(bytes.AsSpan(pos, Math.Min(readSize, bytes.Length - pos)));
        }

        fromBytes.ToArray().Should().Equal(fromFloats.ToArray());
    }
}
