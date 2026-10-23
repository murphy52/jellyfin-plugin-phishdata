using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;

namespace Jellyfin.Plugin.PhishNet.Chapters.Audio;

/// <summary>
/// A decoded track template.
/// </summary>
/// <param name="Envelope">The loudness envelope, at most <see cref="AlignmentConstants.TemplateFrames"/> long.</param>
/// <param name="BytesRead">Bytes taken from the source, to charge against the per-show budget.</param>
/// <param name="HitByteCap">True when feeding stopped at the byte cap rather than because ffmpeg had enough.</param>
public sealed record TemplateDecodeResult(float[] Envelope, long BytesRead, bool HitByteCap)
{
    /// <summary>
    /// Says whether the template is usable: the full 90 s, or for a track shorter than that, all of it
    /// (within 1 s, since phish.in's duration and the decoded length can differ slightly). A capped
    /// download never is.
    /// </summary>
    /// <param name="trackSeconds">The track length phish.in reports.</param>
    /// <returns>True when the template can be aligned and cached.</returns>
    public bool IsCompleteFor(double trackSeconds)
    {
        if (HitByteCap || Envelope.Length == 0)
        {
            return false;
        }

        if (trackSeconds >= AlignmentConstants.TemplateSeconds)
        {
            return Envelope.Length >= AlignmentConstants.TemplateFrames;
        }

        return Envelope.Length >= (int)Math.Floor(trackSeconds * AlignmentConstants.Fps) - AlignmentConstants.Fps;
    }
}

/// <summary>
/// Turns audio into loudness envelopes with ffmpeg.
/// </summary>
public interface IEnvelopeDecoder
{
    /// <summary>
    /// Decodes a whole video's audio.
    /// </summary>
    /// <param name="path">Absolute path of the video.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The envelope.</returns>
    Task<float[]> DecodeFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes the first <see cref="AlignmentConstants.TemplateSeconds"/> of a track, reading no more of
    /// <paramref name="source"/> than ffmpeg needs and never more than <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="source">The track audio, e.g. a phish.in MP3 response body. The caller disposes it, which
    /// also ends a read that ignored cancellation.</param>
    /// <param name="maxBytes">Most bytes to read: the smaller of the per-track cap and what's left of the show's.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template and how much was read.</returns>
    Task<TemplateDecodeResult> DecodeTemplateAsync(Stream source, long maxBytes, CancellationToken cancellationToken);
}
