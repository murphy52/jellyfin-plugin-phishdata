using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;

namespace Jellyfin.Plugin.PhishNet.Chapters.PhishIn;

/// <summary>
/// Keeps each track template's envelope under <c>&lt;directory&gt;/&lt;sha1(mp3 url)&gt;.bin</c>, so
/// re-aligning a show never downloads it again. phish.in MP3 URLs are content-addressed blob keys, so
/// new audio arrives under a new URL and the URL alone is a safe key.
/// </summary>
/// <remarks>
/// Layout, little-endian: "PHTC", cache format version, sample rate, fps, envelope formula version,
/// template seconds, frame count, URL byte length (all int32), the UTF-8 URL, then the float32 frames.
/// A record whose header doesn't match the current settings, or that is damaged, reads as a miss.
/// </remarks>
public sealed class TemplateCache
{
    private const int FormatVersion = 1;
    private const int HeaderInts = 8;
    private const int MaxUrlBytes = 4096;
    private const int MaxRecordBytes = 4 + (HeaderInts * 4) + MaxUrlBytes + (AlignmentConstants.TemplateFrames * 4);
    private static readonly byte[] Magic = "PHTC"u8.ToArray();

    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateCache"/> class.
    /// </summary>
    /// <param name="directory">The cache folder, e.g. <c>&lt;DataPath&gt;/phishdata/phishin</c>; created on first write.</param>
    public TemplateCache(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Reads a cached template.
    /// </summary>
    /// <param name="mp3Url">The track's phish.in MP3 URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The envelope, or null when it isn't cached or the record can't be trusted.</returns>
    public async Task<float[]?> TryReadAsync(string mp3Url, CancellationToken cancellationToken)
    {
        FileStream file;
        try
        {
            file = new FileStream(PathFor(mp3Url), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        await using (file.ConfigureAwait(false))
        {
            // Checked before reading, so a damaged or foreign file can't force a large allocation.
            if (file.Length > MaxRecordBytes)
            {
                return null;
            }

            var bytes = new byte[file.Length];
            await file.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return Parse(bytes, mp3Url);
        }
    }

    /// <summary>
    /// Stores a template, replacing any earlier record atomically.
    /// </summary>
    /// <param name="mp3Url">The track's phish.in MP3 URL.</param>
    /// <param name="envelope">A complete template envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the record is in place.</returns>
    public async Task WriteAsync(string mp3Url, float[] envelope, CancellationToken cancellationToken)
    {
        if (envelope.Length is 0 or > AlignmentConstants.TemplateFrames || !Array.TrueForAll(envelope, float.IsFinite))
        {
            throw new ArgumentException("A template must have 1 to 1800 finite frames", nameof(envelope));
        }

        var url = Encoding.UTF8.GetBytes(mp3Url);
        if (url.Length > MaxUrlBytes)
        {
            throw new ArgumentException("URL too long to cache", nameof(mp3Url));
        }

        var bytes = new byte[Magic.Length + (HeaderInts * 4) + url.Length + (envelope.Length * 4)];
        var span = bytes.AsSpan();
        Magic.CopyTo(span);
        int pos = Magic.Length;
        foreach (var value in ExpectedHeader())
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], value);
            pos += 4;
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[pos..], envelope.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[(pos + 4)..], url.Length);
        pos += 8;
        url.CopyTo(span[pos..]);
        pos += url.Length;
        foreach (var frame in envelope)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[pos..], frame);
            pos += 4;
        }

        Directory.CreateDirectory(_directory);
        var path = PathFor(mp3Url);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp); // no-op once moved
        }
    }

    private static int[] ExpectedHeader() =>
    [
        FormatVersion,
        AlignmentConstants.SampleRate,
        AlignmentConstants.Fps,
        EnvelopeBuilder.FormulaVersion,
        AlignmentConstants.TemplateSeconds,
    ];

    private static float[]? Parse(byte[] bytes, string mp3Url)
    {
        var span = bytes.AsSpan();
        int fixedLength = Magic.Length + (HeaderInts * 4);
        if (span.Length < fixedLength || !span[..Magic.Length].SequenceEqual(Magic))
        {
            return null;
        }

        int pos = Magic.Length;
        foreach (var expected in ExpectedHeader())
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(span[pos..]) != expected)
            {
                return null;
            }

            pos += 4;
        }

        int frames = BinaryPrimitives.ReadInt32LittleEndian(span[pos..]);
        int urlLength = BinaryPrimitives.ReadInt32LittleEndian(span[(pos + 4)..]);
        pos += 8;
        if (frames is <= 0 or > AlignmentConstants.TemplateFrames || urlLength is < 0 or > MaxUrlBytes
            || span.Length != fixedLength + urlLength + (frames * 4)
            || !span.Slice(pos, urlLength).SequenceEqual(Encoding.UTF8.GetBytes(mp3Url)))
        {
            return null;
        }

        pos += urlLength;
        var envelope = new float[frames];
        for (int i = 0; i < frames; i++, pos += 4)
        {
            envelope[i] = BinaryPrimitives.ReadSingleLittleEndian(span[pos..]);
            if (!float.IsFinite(envelope[i]))
            {
                return null;
            }
        }

        return envelope;
    }

    private string PathFor(string mp3Url)
    {
        // SHA-1 only names the file; the URL stored in the record is what's trusted.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(mp3Url))).ToLowerInvariant();
        return Path.Combine(_directory, hash + ".bin");
    }
}
