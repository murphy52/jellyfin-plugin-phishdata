using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhishNet.Chapters.Audio;

/// <summary>
/// ffmpeg failed or produced no audio.
/// </summary>
public sealed class EnvelopeDecodeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnvelopeDecodeException"/> class.
    /// </summary>
    /// <param name="message">What went wrong, with the tail of ffmpeg's stderr.</param>
    public EnvelopeDecodeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Runs Jellyfin's ffmpeg (<see cref="IMediaEncoder.EncoderPath"/>) to decode mono 8 kHz float PCM and
/// streams it into an <see cref="EnvelopeBuilder"/>, as phish_core.decode_env does but without buffering
/// the PCM. ffmpeg runs at below-normal priority and is killed on timeout or cancel.
/// </summary>
public sealed class EnvelopeDecoder : IEnvelopeDecoder
{
    /// <summary>Most bytes read for one track template.</summary>
    public const long MaxTrackBytes = 8L * 1024 * 1024;

    /// <summary>Most bytes read for all of one show's templates.</summary>
    public const long MaxShowBytes = 150L * 1024 * 1024;

    private const int ChunkBytes = 64 * 1024;
    private const int StderrTailChars = 2000;

    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<EnvelopeDecoder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvelopeDecoder"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Jellyfin's media encoder, for the ffmpeg path.</param>
    /// <param name="logger">Logger.</param>
    public EnvelopeDecoder(IMediaEncoder mediaEncoder, ILogger<EnvelopeDecoder> logger)
    {
        _mediaEncoder = mediaEncoder;
        _logger = logger;
    }

    /// <summary>Gets the longest a whole-video decode may take.</summary>
    public TimeSpan VideoTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Gets the longest one track template may take, download included.</summary>
    public TimeSpan TemplateTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets a hook tests use to watch the ffmpeg process.</summary>
    internal Action<Process>? ProcessStarted { get; init; }

    /// <inheritdoc />
    public async Task<float[]> DecodeFileAsync(string path, CancellationToken cancellationToken)
    {
        // An absolute path can't start with '-', so ffmpeg never reads it as an option.
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"Not an absolute path: {path}", nameof(path));
        }

        var run = await RunAsync(["-nostdin", "-v", "error", "-i", path, .. OutputArgs], path, null, 0, VideoTimeout, cancellationToken)
            .ConfigureAwait(false);
        return run.Envelope;
    }

    /// <inheritdoc />
    public Task<TemplateDecodeResult> DecodeTemplateAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        // Streamed through stdin rather than opened by ffmpeg, so this process controls the download:
        // once ffmpeg has its 90 s it exits, the next write fails, and the rest of the MP3 is never fetched.
        var seconds = AlignmentConstants.TemplateSeconds.ToString(CultureInfo.InvariantCulture);
        return RunAsync(["-v", "error", "-t", seconds, "-i", "pipe:0", .. OutputArgs], "track", source, maxBytes, TemplateTimeout, cancellationToken);
    }

    private static string[] OutputArgs =>
        ["-ac", "1", "-ar", AlignmentConstants.SampleRate.ToString(CultureInfo.InvariantCulture), "-f", "f32le", "-"];

    private async Task<TemplateDecodeResult> RunAsync(
        IReadOnlyList<string> args,
        string label,
        Stream? source,
        long maxBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var ffmpeg = _mediaEncoder.EncoderPath;
        if (string.IsNullOrEmpty(ffmpeg))
        {
            throw new EnvelopeDecodeException("Jellyfin has no ffmpeg configured");
        }

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var feedCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        using var process = new Process { StartInfo = psi };
        process.Start();
        LowerPriority(process);
        ProcessStarted?.Invoke(process);

        // Pipe reads don't honor cancellation everywhere, so a cancel kills ffmpeg, which closes the pipes.
        using var kill = linked.Token.Register(() => Kill(process));

        var stderr = ReadTailAsync(process.StandardError);
        var feed = source is null
            ? CloseInput(process)
            : FeedAsync(source, process.StandardInput.BaseStream, maxBytes, feedCts.Token);

        var builder = new EnvelopeBuilder();
        var buffer = new byte[ChunkBytes];
        var stdout = process.StandardOutput.BaseStream;
        try
        {
            int n;
            while ((n = await stdout.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                builder.AddBytes(buffer.AsSpan(0, n));
            }
        }
        catch (IOException) when (linked.IsCancellationRequested)
        {
            // The pipe broke because ffmpeg was killed; reported below.
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await feedCts.CancelAsync().ConfigureAwait(false); // a feeder still waiting on the network
        var tail = await stderr.ConfigureAwait(false);

        FeedResult fed;
        try
        {
            fed = await feed.ConfigureAwait(false);
        }
        catch when (linked.IsCancellationRequested)
        {
            fed = default;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"ffmpeg took longer than {timeout} on {label}");
        }

        var envelope = builder.ToArray();
        if (!fed.HitByteCap && (process.ExitCode != 0 || envelope.Length == 0))
        {
            throw new EnvelopeDecodeException(
                $"ffmpeg could not decode {label} (exit {process.ExitCode}, {envelope.Length} frames): {tail}");
        }

        if (tail.Length > 0)
        {
            _logger.LogDebug("ffmpeg on {Label}: {Stderr}", label, tail);
        }

        return new TemplateDecodeResult(envelope, fed.BytesRead, fed.HitByteCap);
    }

    private static async Task<FeedResult> FeedAsync(Stream source, Stream stdin, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkBytes];
        long total = 0;
        bool capped = false;
        try
        {
            while (true)
            {
                if (total >= maxBytes)
                {
                    capped = true;
                    break;
                }

                int want = (int)Math.Min(buffer.Length, maxBytes - total);
                int n = await source.ReadAsync(buffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                total += n;
                try
                {
                    await stdin.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    break; // ffmpeg has its 90 s and closed stdin: stop downloading
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // ffmpeg finished (or was killed) while the download was still waiting.
        }
        finally
        {
            try
            {
                await stdin.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Already closed by ffmpeg.
            }
        }

        return new FeedResult(total, capped);
    }

    private static Task<FeedResult> CloseInput(Process process)
    {
        process.StandardInput.Close();
        return Task.FromResult(default(FeedResult));
    }

    private static async Task<string> ReadTailAsync(StreamReader stderr)
    {
        var tail = new StringBuilder();
        var buffer = new char[1024];
        int n;
        while ((n = await stderr.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            tail.Append(buffer, 0, n);
            if (tail.Length > StderrTailChars)
            {
                tail.Remove(0, tail.Length - StderrTailChars);
            }
        }

        return tail.ToString().Trim();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private void LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogDebug(ex, "Could not lower ffmpeg's priority");
        }
    }

    private readonly record struct FeedResult(long BytesRead, bool HitByteCap);
}
