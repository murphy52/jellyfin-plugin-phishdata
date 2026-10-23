using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.PhishNet.Chapters.Alignment;

/// <summary>
/// In-place iterative radix-2 complex FFT. Small on purpose: a numerics package would have to ship in
/// the release zip, and this is tested directly against a naive DFT.
/// </summary>
public static class Fft
{
    private static readonly ConcurrentDictionary<int, (double[] Cos, double[] Sin)> Twiddles = new();

    /// <summary>
    /// Transforms <paramref name="re"/> and <paramref name="im"/> in place. The inverse is scaled by 1/n.
    /// </summary>
    /// <param name="re">Real parts; length must be a power of two.</param>
    /// <param name="im">Imaginary parts; same length as <paramref name="re"/>.</param>
    /// <param name="inverse">True for the inverse transform.</param>
    public static void Transform(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        if (n != im.Length || n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("FFT length must be a power of two and both arrays the same length.");
        }

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        var (cos, sin) = Twiddles.GetOrAdd(n, BuildTwiddles);
        double sign = inverse ? 1.0 : -1.0;
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1;
            int step = n / len;
            for (int start = 0; start < n; start += len)
            {
                for (int k = 0; k < half; k++)
                {
                    double wr = cos[k * step];
                    double wi = sign * sin[k * step];
                    int a = start + k, b = a + half;
                    double xr = (re[b] * wr) - (im[b] * wi);
                    double xi = (re[b] * wi) + (im[b] * wr);
                    re[b] = re[a] - xr;
                    im[b] = im[a] - xi;
                    re[a] += xr;
                    im[a] += xi;
                }
            }
        }

        if (inverse)
        {
            double scale = 1.0 / n;
            for (int i = 0; i < n; i++)
            {
                re[i] *= scale;
                im[i] *= scale;
            }
        }
    }

    /// <summary>
    /// Smallest power of two at or above <paramref name="value"/>.
    /// </summary>
    /// <param name="value">Minimum length.</param>
    /// <returns>The power of two.</returns>
    public static int NextPowerOfTwo(int value)
    {
        int n = 1;
        while (n < value)
        {
            n <<= 1;
        }

        return n;
    }

    private static (double[] Cos, double[] Sin) BuildTwiddles(int n)
    {
        var cos = new double[n / 2];
        var sin = new double[n / 2];
        for (int k = 0; k < n / 2; k++)
        {
            double angle = 2.0 * Math.PI * k / n;
            cos[k] = Math.Cos(angle);
            sin[k] = Math.Sin(angle);
        }

        return (cos, sin);
    }
}
