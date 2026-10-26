using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The FFT normalized cross-correlation must match a direct computation of the same formula,
/// including the silent-window guard ported from phish_core.ncc_search.
/// </summary>
public class NccTests
{
    [Fact]
    public void Fft_ShouldMatchNaiveDft()
    {
        var rng = new Random(1);
        const int n = 64;
        var re = Enumerable.Range(0, n).Select(_ => rng.NextDouble() - 0.5).ToArray();
        var im = Enumerable.Range(0, n).Select(_ => rng.NextDouble() - 0.5).ToArray();
        var (expectRe, expectIm) = NaiveDft(re, im);

        var gotRe = (double[])re.Clone();
        var gotIm = (double[])im.Clone();
        Fft.Transform(gotRe, gotIm, inverse: false);

        for (int k = 0; k < n; k++)
        {
            gotRe[k].Should().BeApproximately(expectRe[k], 1e-9);
            gotIm[k].Should().BeApproximately(expectIm[k], 1e-9);
        }
    }

    [Fact]
    public void Fft_InverseShouldRoundTrip()
    {
        var rng = new Random(2);
        var re = Enumerable.Range(0, 256).Select(_ => rng.NextDouble()).ToArray();
        var im = new double[256];
        var origin = (double[])re.Clone();

        Fft.Transform(re, im, inverse: false);
        Fft.Transform(re, im, inverse: true);

        for (int i = 0; i < re.Length; i++)
        {
            re[i].Should().BeApproximately(origin[i], 1e-9);
            im[i].Should().BeApproximately(0, 1e-9);
        }
    }

    [Fact]
    public void Correlate_ShouldMatchDirectFormula()
    {
        var rng = new Random(3);
        var signal = RandomEnvelope(rng, 3000);
        var template = RandomEnvelope(rng, 150);

        var profile = new NccSearcher(signal, 200).Correlate(template);
        var expected = DirectNcc(signal, template);

        profile.Scores.Length.Should().Be(expected.Length);
        for (int k = 0; k < expected.Length; k++)
        {
            profile.Scores[k].Should().BeApproximately(expected[k], 1e-6, "offset {0}", k);
        }
    }

    [Fact]
    public void Search_ShouldFindPlantedTemplate()
    {
        var rng = new Random(4);
        var signal = RandomEnvelope(rng, 5000);
        var template = signal.Skip(1234).Take(300).ToArray();

        var (index, score) = new NccSearcher(signal, 300).Correlate(template).Search(0, null);

        index.Should().Be(1234);
        score.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void Search_ShouldNotMatchDigitalSilence()
    {
        var rng = new Random(5);
        var signal = RandomEnvelope(rng, 4000);
        Array.Fill(signal, 0.5f, 1000, 1000); // a setbreak: perfectly flat loudness
        var template = RandomEnvelope(rng, 200);

        var profile = new NccSearcher(signal, 200).Correlate(template);

        for (int k = 1000; k <= 2000 - 200; k++)
        {
            profile.Scores[k].Should().Be(0.0);
        }

        profile.Scores.Should().OnlyContain(s => Math.Abs(s) <= 1.0 + 1e-6);
    }

    [Fact]
    public void Search_ShouldRespectWindowBounds()
    {
        var rng = new Random(6);
        var signal = RandomEnvelope(rng, 5000);
        var template = signal.Skip(4000).Take(300).ToArray();
        var profile = new NccSearcher(signal, 300).Correlate(template);

        var (index, _) = profile.Search(100, 2000);

        index.Should().BeInRange(100, 2000);
    }

    [Fact]
    public void Search_ShouldReturnLoWithZero_WhenWindowStartsPastTheEnd()
    {
        var rng = new Random(7);
        var signal = RandomEnvelope(rng, 1000);
        var profile = new NccSearcher(signal, 100).Correlate(RandomEnvelope(rng, 100));

        profile.Search(950, 2000).Should().Be((950, 0.0));
    }

    [Fact]
    public void Search_ShouldReturnZero_ForTinyTemplate()
    {
        var rng = new Random(8);
        var signal = RandomEnvelope(rng, 1000);

        new NccSearcher(signal, 100).Correlate(new[] { 1f, 2f, 3f }).Search(10, null).Should().Be((10, 0.0));
    }

    [Fact]
    public void Correlate_ShouldScoreZero_WhenTheSignalIsNotFinite()
    {
        var rng = new Random(9);
        var signal = RandomEnvelope(rng, 2000);
        signal[500] = float.NaN;

        var profile = new NccSearcher(signal, 200).Correlate(RandomEnvelope(rng, 200));

        profile.Scores.Should().OnlyContain(s => s == 0.0, "a NaN score must fail closed, not slip past the > 1 check");
    }

    [Fact]
    public void NextPowerOfTwo_ShouldRoundUp()
    {
        Fft.NextPowerOfTwo(1).Should().Be(1);
        Fft.NextPowerOfTwo(3).Should().Be(4);
        Fft.NextPowerOfTwo(1 << 30).Should().Be(1 << 30);
    }

    [Fact]
    public void NextPowerOfTwo_ShouldThrow_AboveTheLargestIntPowerOfTwo()
    {
        var act = () => Fft.NextPowerOfTwo((1 << 30) + 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    internal static float[] RandomEnvelope(Random rng, int frames)
    {
        // Smoothed noise looks more like a loudness curve than white noise does.
        var e = new float[frames];
        double v = 1.0;
        for (int i = 0; i < frames; i++)
        {
            v = 0.9 * v + 0.1 * (rng.NextDouble() * 2.0);
            e[i] = (float)(v + 0.2 * rng.NextDouble());
        }

        return e;
    }

    private static double[] DirectNcc(float[] sig, float[] tmpl)
    {
        int l = tmpl.Length, n = sig.Length;
        double mean = tmpl.Average(x => (double)x);
        var t0 = tmpl.Select(x => x - mean).ToArray();
        double dt = Math.Sqrt(t0.Sum(x => x * x)) + 1e-9;
        var ds = new double[n - l + 1];
        var ncc = new double[n - l + 1];
        for (int k = 0; k <= n - l; k++)
        {
            double num = 0, w1 = 0, w2 = 0;
            for (int j = 0; j < l; j++)
            {
                num += sig[k + j] * t0[j];
                w1 += sig[k + j];
                w2 += (double)sig[k + j] * sig[k + j];
            }

            ds[k] = Math.Sqrt(Math.Max(w2 - (w1 * w1 / l), 0));
            ncc[k] = num / ((ds[k] + 1e-9) * dt);
        }

        var sorted = ds.OrderBy(x => x).ToArray();
        int m = sorted.Length;
        double typ = m % 2 == 1 ? sorted[m / 2] : (sorted[(m / 2) - 1] + sorted[m / 2]) / 2;
        for (int k = 0; k < ncc.Length; k++)
        {
            if (ds[k] < Math.Max(1e-6, 0.05 * typ) || Math.Abs(ncc[k]) > 1.0 + 1e-6)
            {
                ncc[k] = 0;
            }
        }

        return ncc;
    }

    private static (double[] Re, double[] Im) NaiveDft(double[] re, double[] im)
    {
        int n = re.Length;
        var outRe = new double[n];
        var outIm = new double[n];
        for (int k = 0; k < n; k++)
        {
            for (int t = 0; t < n; t++)
            {
                double a = -2 * Math.PI * k * t / n;
                outRe[k] += (re[t] * Math.Cos(a)) - (im[t] * Math.Sin(a));
                outIm[k] += (re[t] * Math.Sin(a)) + (im[t] * Math.Cos(a));
            }
        }

        return (outRe, outIm);
    }
}
