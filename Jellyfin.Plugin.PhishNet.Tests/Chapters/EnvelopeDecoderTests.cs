using System.Diagnostics;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The decoder streams ffmpeg's PCM into the envelope, feeds track audio through stdin, and stops
/// downloading once 90 s is decoded. ffmpeg must never outlive a timeout or a cancel.
/// </summary>
public class EnvelopeDecoderTests : IDisposable
{
    private const int Rate = AlignmentConstants.SampleRate;

    private readonly string _dir = Directory.CreateTempSubdirectory("phish-decoder-").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [FfmpegFact]
    public async Task DecodeFile_ShouldMatchTheBuilderOnTheSameSamples()
    {
        var samples = Signal(seconds: 5);
        var path = Path.Combine(_dir, "show.wav");
        await File.WriteAllBytesAsync(path, Wav(samples));

        var env = await Create().DecodeFileAsync(path, CancellationToken.None);

        env.Should().HaveCount(5 * AlignmentConstants.Fps);
        env.Should().Equal(Expected(samples), (a, b) => Math.Abs(a - b) < 1e-5f);
    }

    [FfmpegFact]
    public async Task DecodeTemplate_ShouldStopAtNinetySecondsAndLeaveTheRestUnread()
    {
        var samples = Signal(seconds: 240);
        var wav = Wav(samples);
        using var source = new MemoryStream(wav);

        var result = await Create().DecodeTemplateAsync(source, EnvelopeDecoder.MaxTrackBytes, CancellationToken.None);

        result.Envelope.Should().HaveCount(AlignmentConstants.TemplateFrames);
        result.Envelope.Should().Equal(Expected(samples)[..AlignmentConstants.TemplateFrames], (a, b) => Math.Abs(a - b) < 1e-5f);
        result.HitByteCap.Should().BeFalse();
        result.BytesRead.Should().BeLessThan(wav.Length * 3 / 4, "the feeder stops once ffmpeg has its 90 s");
        result.IsCompleteFor(trackSeconds: 240).Should().BeTrue();
    }

    [FfmpegFact]
    public async Task DecodeTemplate_ShouldReportTheByteCap_AndTheTemplateIsThenIncomplete()
    {
        using var source = new MemoryStream(Wav(Signal(seconds: 120)));

        var result = await Create().DecodeTemplateAsync(source, maxBytes: 100_000, CancellationToken.None);

        result.HitByteCap.Should().BeTrue();
        result.BytesRead.Should().Be(100_000);
        result.Envelope.Length.Should().BeInRange(100, 130, "100 KB of 16 kB/s audio is about 6 s");
        result.IsCompleteFor(trackSeconds: 120).Should().BeFalse();
    }

    [FfmpegFact]
    public async Task DecodeTemplate_ShouldKillFfmpegAndThrowTimeout_WhenTheSourceStalls()
    {
        int pid = 0;
        var decoder = Create(p => pid = p.Id, templateTimeout: TimeSpan.FromMilliseconds(500));
        using var source = new StallingStream(Wav(Signal(seconds: 1))[..44]);

        var act = () => decoder.DecodeTemplateAsync(source, EnvelopeDecoder.MaxTrackBytes, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        IsRunning(pid).Should().BeFalse("ffmpeg must not outlive the decode");
    }

    [FfmpegFact]
    public async Task DecodeTemplate_ShouldKillFfmpegAndThrowCanceled_WhenTheCallerCancels()
    {
        int pid = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        using var source = new StallingStream(Wav(Signal(seconds: 1))[..44]);

        var act = () => Create(p => pid = p.Id).DecodeTemplateAsync(source, EnvelopeDecoder.MaxTrackBytes, cts.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cts.Token);
        IsRunning(pid).Should().BeFalse("ffmpeg must not outlive the decode");
    }

    [FfmpegFact]
    public async Task DecodeTemplate_ShouldNotHang_WhenTheSourceIgnoresCancellation()
    {
        var decoder = Create(templateTimeout: TimeSpan.FromMilliseconds(300), feederGrace: TimeSpan.FromMilliseconds(200));
        using var source = new DeafStream(Wav(Signal(seconds: 1))[..44]);
        var clock = Stopwatch.StartNew();

        var act = () => decoder.DecodeTemplateAsync(source, EnvelopeDecoder.MaxTrackBytes, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Decode_ShouldThrowDecodeException_WhenFfmpegCanNotStart()
    {
        using var source = new MemoryStream(new byte[10]);

        var act = () => Create("/nonexistent/ffmpeg").DecodeTemplateAsync(source, EnvelopeDecoder.MaxTrackBytes, CancellationToken.None);

        await act.Should().ThrowAsync<EnvelopeDecodeException>().WithMessage("*/nonexistent/ffmpeg*");
    }

    [FfmpegFact]
    public async Task DecodeFile_ShouldThrowWithFfmpegsMessage_ForAFileItCannotDecode()
    {
        var path = Path.Combine(_dir, "not-audio.mkv");
        await File.WriteAllTextAsync(path, "this is not a video");

        var act = () => Create().DecodeFileAsync(path, CancellationToken.None);

        (await act.Should().ThrowAsync<EnvelopeDecodeException>()).Which.Message.Should().Contain("not-audio.mkv");
    }

    [FfmpegFact]
    public async Task DecodeTemplate_ShouldPassOnAReadError()
    {
        using var source = new FailingStream();

        var act = () => Create().DecodeTemplateAsync(source, EnvelopeDecoder.MaxTrackBytes, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Decode_ShouldThrow_WhenJellyfinHasNoFfmpeg()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(e => e.EncoderPath).Returns(string.Empty);
        var decoder = new EnvelopeDecoder(encoder.Object, NullLogger<EnvelopeDecoder>.Instance);

        var act = () => decoder.DecodeFileAsync("/videos/show.mkv", CancellationToken.None);

        await act.Should().ThrowAsync<EnvelopeDecodeException>().WithMessage("*ffmpeg*");
    }

    [Fact]
    public async Task DecodeFile_ShouldRejectARelativePath_SoItCanNotBeReadAsAnOption()
    {
        var act = () => Create("/usr/bin/ffmpeg").DecodeFileAsync("-i", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(1800, 300, true)]
    [InlineData(1799, 300, false)]
    [InlineData(1800, 90, true)]
    [InlineData(1780, 90, false)]
    [InlineData(1778, 89.9, true)]
    [InlineData(500, 25, true)]
    [InlineData(480, 25, true)]
    [InlineData(479, 25, false)]
    [InlineData(0, 25, false)]
    public void IsCompleteFor_ShouldWantNinetySeconds_OrTheWholeOfAShorterTrack(int frames, double trackSeconds, bool expected)
    {
        new TemplateDecodeResult(new float[frames], 0, false).IsCompleteFor(trackSeconds).Should().Be(expected);
    }

    [Fact]
    public void IsCompleteFor_ShouldBeFalse_WhenTheByteCapWasHit()
    {
        new TemplateDecodeResult(new float[1800], 8_000_000, true).IsCompleteFor(300).Should().BeFalse();
    }

    private static bool IsRunning(int pid)
    {
        pid.Should().NotBe(0);
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static EnvelopeDecoder Create(Action<Process>? started = null, TimeSpan? templateTimeout = null, TimeSpan? feederGrace = null) =>
        Create(FfmpegFactAttribute.FfmpegPath!, started, templateTimeout, feederGrace);

    private static EnvelopeDecoder Create(string ffmpeg, Action<Process>? started = null, TimeSpan? templateTimeout = null, TimeSpan? feederGrace = null)
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(e => e.EncoderPath).Returns(ffmpeg);
        return new EnvelopeDecoder(encoder.Object, NullLogger<EnvelopeDecoder>.Instance)
        {
            ProcessStarted = started,
            TemplateTimeout = templateTimeout ?? TimeSpan.FromMinutes(2),
            FeederGrace = feederGrace ?? TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>A 440 Hz tone whose loudness changes every second, as 16-bit samples.</summary>
    private static short[] Signal(int seconds)
    {
        var rng = new Random(26);
        var s = new short[seconds * Rate];
        double gain = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (i % Rate == 0)
            {
                gain = rng.NextDouble() * 0.8;
            }

            s[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * gain * short.MaxValue);
        }

        return s;
    }

    private static float[] Expected(short[] samples)
    {
        var builder = new EnvelopeBuilder();
        builder.Add(samples.Select(v => v / 32768f).ToArray());
        return builder.ToArray();
    }

    private static byte[] Wav(short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int data = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + data);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(data);
        foreach (var v in samples)
        {
            w.Write(v);
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Returns its bytes, then waits until canceled, like a stalled download.</summary>
    private sealed class StallingStream(byte[] head) : MemoryStream(head)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await base.ReadAsync(buffer, cancellationToken);
            if (n > 0)
            {
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    /// <summary>Returns its bytes, then blocks until disposed, ignoring cancellation.</summary>
    private sealed class DeafStream(byte[] head) : MemoryStream(head)
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await base.ReadAsync(buffer, CancellationToken.None);
            if (n > 0)
            {
                return n;
            }

            await _disposed.Task;
            throw new ObjectDisposedException(nameof(DeafStream));
        }

        protected override void Dispose(bool disposing)
        {
            _disposed.TrySetResult();
            base.Dispose(disposing);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new HttpRequestException("connection reset"));
    }
}

/// <summary>A test that needs ffmpeg; skipped when none is found.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (FfmpegPath is null)
        {
            Skip = "ffmpeg not found on PATH";
        }
    }

    public static string? FfmpegPath { get; } = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(dir => Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"))
        .FirstOrDefault(File.Exists);
}
