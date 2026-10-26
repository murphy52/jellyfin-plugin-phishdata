using System;

namespace Jellyfin.Plugin.PhishNet.Chapters.Alignment;

/// <summary>
/// Normalized cross-correlation of templates against one signal (a show's loudness envelope).
/// The signal's transform and prefix sums are computed once and reused for every template.
/// Not thread-safe: it reuses work buffers between calls.
/// </summary>
public sealed class NccSearcher
{
    private readonly float[] _signal;
    private readonly int _maxTemplateLength;
    private readonly double[] _signalRe;
    private readonly double[] _signalIm;
    private readonly double[] _workRe;
    private readonly double[] _workIm;
    private readonly double[] _sum;
    private readonly double[] _sumSquares;
    private readonly double[] _ds;
    private readonly double[] _dsSorted;

    /// <summary>
    /// Initializes a new instance of the <see cref="NccSearcher"/> class.
    /// </summary>
    /// <param name="signal">The envelope to search in.</param>
    /// <param name="maxTemplateLength">Longest template that will be correlated, in frames.</param>
    public NccSearcher(float[] signal, int maxTemplateLength)
    {
        _signal = signal;
        _maxTemplateLength = maxTemplateLength;
        int nfft = Fft.NextPowerOfTwo(Math.Max(2, signal.Length + maxTemplateLength));
        _signalRe = new double[nfft];
        _signalIm = new double[nfft];
        for (int i = 0; i < signal.Length; i++)
        {
            _signalRe[i] = signal[i];
        }

        Fft.Transform(_signalRe, _signalIm, inverse: false);
        _workRe = new double[nfft];
        _workIm = new double[nfft];

        _ds = new double[signal.Length];
        _dsSorted = new double[signal.Length];
        _sum = new double[signal.Length + 1];
        _sumSquares = new double[signal.Length + 1];
        for (int i = 0; i < signal.Length; i++)
        {
            _sum[i + 1] = _sum[i] + signal[i];
            _sumSquares[i + 1] = _sumSquares[i] + ((double)signal[i] * signal[i]);
        }
    }

    /// <summary>
    /// Correlates a template at every offset of the signal.
    /// </summary>
    /// <param name="template">The template envelope.</param>
    /// <returns>The NCC profile, searchable by window.</returns>
    public NccProfile Correlate(float[] template)
    {
        int l = template.Length, n = _signal.Length;
        if (l > _maxTemplateLength)
        {
            throw new ArgumentException($"Template of {l} frames exceeds the {_maxTemplateLength} this searcher was built for.", nameof(template));
        }

        if (l < 4 || l > n)
        {
            return new NccProfile(Array.Empty<double>(), l, n);
        }

        double mean = 0;
        for (int j = 0; j < l; j++)
        {
            mean += template[j];
        }

        mean /= l;
        Array.Clear(_workRe);
        Array.Clear(_workIm);
        double energy = 0;
        for (int j = 0; j < l; j++)
        {
            double t0 = template[j] - mean;
            energy += t0 * t0;
            _workRe[l - 1 - j] = t0; // reversed: convolution with the reversed template is correlation
        }

        double dt = Math.Sqrt(energy) + 1e-9;
        Fft.Transform(_workRe, _workIm, inverse: false);
        for (int k = 0; k < _workRe.Length; k++)
        {
            double r = (_workRe[k] * _signalRe[k]) - (_workIm[k] * _signalIm[k]);
            double i = (_workRe[k] * _signalIm[k]) + (_workIm[k] * _signalRe[k]);
            _workRe[k] = r;
            _workIm[k] = i;
        }

        Fft.Transform(_workRe, _workIm, inverse: true);

        int count = n - l + 1;
        var scores = new double[count];
        var ds = _ds;
        for (int k = 0; k < count; k++)
        {
            double w1 = _sum[k + l] - _sum[k];
            double w2 = _sumSquares[k + l] - _sumSquares[k];
            ds[k] = Math.Sqrt(Math.Max(w2 - (w1 * w1 / l), 0));
            scores[k] = _workRe[l - 1 + k] / ((ds[k] + 1e-9) * dt);
        }

        // A digitally silent window (a setbreak) has zero variance, so FFT residue in the numerator
        // blows up into scores in the hundreds and every template "wins" there. Zero those windows;
        // clipping to 1.0 would turn the artefact into a perfect match instead.
        ds.AsSpan(0, count).CopyTo(_dsSorted);
        double typical = Stats.MedianInPlace(_dsSorted.AsSpan(0, count));
        double floor = Math.Max(1e-6, 0.05 * typical);
        for (int k = 0; k < count; k++)
        {
            // Written so a NaN score (non-finite input) is zeroed too: NaN > 1 is false.
            if (ds[k] < floor || !(Math.Abs(scores[k]) <= 1.0 + 1e-6))
            {
                scores[k] = 0.0;
            }
        }

        return new NccProfile(scores, l, n);
    }
}

/// <summary>
/// One template's NCC at every offset of the signal.
/// </summary>
public sealed class NccProfile
{
    private readonly int _templateLength;
    private readonly int _signalLength;

    internal NccProfile(double[] scores, int templateLength, int signalLength)
    {
        Scores = scores;
        _templateLength = templateLength;
        _signalLength = signalLength;
    }

    /// <summary>Gets the NCC at each offset (length signal - template + 1, or empty).</summary>
    public double[] Scores { get; }

    /// <summary>
    /// Best offset within [lo, hi], with the same edge behaviour as phish_core.ncc_search.
    /// </summary>
    /// <param name="lo">First offset to consider, in frames.</param>
    /// <param name="hi">Last offset to consider, or null for the end of the signal.</param>
    /// <returns>The best offset and its score; (lo, 0) when the window is empty.</returns>
    public (int Index, double Score) Search(int lo, int? hi)
    {
        if (_templateLength < 4 || _signalLength < _templateLength + lo + 1)
        {
            return (lo, 0.0);
        }

        int top = Scores.Length - 1;
        int last = hi is null || hi.Value > top ? top : hi.Value;
        lo = Math.Max(0, lo);
        if (lo > last)
        {
            return (lo, 0.0);
        }

        int best = lo;
        for (int k = lo + 1; k <= last; k++)
        {
            if (Scores[k] > Scores[best])
            {
                best = k;
            }
        }

        return (best, Scores[best]);
    }
}
