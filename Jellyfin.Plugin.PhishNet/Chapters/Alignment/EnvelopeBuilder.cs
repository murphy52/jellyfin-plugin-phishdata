using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Jellyfin.Plugin.PhishNet.Chapters.Alignment;

/// <summary>
/// Turns mono PCM at <see cref="AlignmentConstants.SampleRate"/> into the loudness envelope
/// (50 ms RMS frames, log1p(rms * 20)) as it streams in, so a 3-hour show needs about 0.9 MB rather
/// than 360 MB of buffered samples. A trailing partial hop is dropped, as in phish_core.decode_env.
/// </summary>
public sealed class EnvelopeBuilder
{
    private const int Hop = AlignmentConstants.SampleRate / AlignmentConstants.Fps;

    private readonly List<float> _frames = new();
    private readonly byte[] _pendingBytes = new byte[4];
    private int _pendingByteCount;
    private double _hopSumSquares;
    private int _hopCount;

    /// <summary>Gets the number of complete frames so far.</summary>
    public int FrameCount => _frames.Count;

    /// <summary>
    /// Adds samples.
    /// </summary>
    /// <param name="samples">PCM samples in [-1, 1].</param>
    public void Add(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            AddSample(s);
        }
    }

    /// <summary>
    /// Adds raw little-endian 32-bit float PCM (ffmpeg's f32le), which may split a sample across calls.
    /// </summary>
    /// <param name="bytes">The bytes read so far.</param>
    public void AddBytes(ReadOnlySpan<byte> bytes)
    {
        while (_pendingByteCount > 0 && bytes.Length > 0)
        {
            _pendingBytes[_pendingByteCount++] = bytes[0];
            bytes = bytes[1..];
            if (_pendingByteCount == 4)
            {
                AddSample(BinaryPrimitives.ReadSingleLittleEndian(_pendingBytes));
                _pendingByteCount = 0;
            }
        }

        if (bytes.IsEmpty)
        {
            return; // a sample may still be partly pending
        }

        int whole = bytes.Length / 4 * 4;
        for (int i = 0; i < whole; i += 4)
        {
            AddSample(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(i, 4)));
        }

        bytes[whole..].CopyTo(_pendingBytes);
        _pendingByteCount = bytes.Length - whole;
    }

    /// <summary>
    /// Returns the envelope built so far.
    /// </summary>
    /// <returns>One value per complete 50 ms frame.</returns>
    public float[] ToArray() => _frames.ToArray();

    private void AddSample(float sample)
    {
        _hopSumSquares += (double)sample * sample;
        if (++_hopCount == Hop)
        {
            _frames.Add((float)Math.Log(1.0 + (Math.Sqrt((_hopSumSquares / Hop) + 1e-9) * 20.0)));
            _hopSumSquares = 0;
            _hopCount = 0;
        }
    }
}
