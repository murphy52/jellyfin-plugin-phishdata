using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// Templates are cached so a show is never downloaded twice. A record that doesn't match the current
/// envelope settings, or is damaged, reads as a miss so the caller re-fetches it.
/// </summary>
public class TemplateCacheTests : IDisposable
{
    private const string Url = "https://phish.in/blob/5sojbd9ou64tg3lz3ko8bwj18zji.mp3";

    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("phish-cache-").FullName, "phishin");

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Write_ThenRead_ShouldReturnTheSameEnvelope()
    {
        var cache = new TemplateCache(_dir);
        var env = Envelope(1800);

        await cache.WriteAsync(Url, env, CancellationToken.None);
        var read = await cache.TryReadAsync(Url, CancellationToken.None);

        read.Should().Equal(env);
    }

    [Fact]
    public async Task Write_ShouldNameTheFileBySha1OfTheUrl_AndLeaveNoTempFiles()
    {
        await new TemplateCache(_dir).WriteAsync(Url, Envelope(10), CancellationToken.None);

        var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Url))).ToLowerInvariant() + ".bin";
        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().Equal(name);
    }

    [Fact]
    public async Task Read_ShouldMiss_WhenNothingIsCached()
    {
        (await new TemplateCache(_dir).TryReadAsync(Url, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Write_ShouldReplaceAnEarlierRecord()
    {
        var cache = new TemplateCache(_dir);
        await cache.WriteAsync(Url, Envelope(1800), CancellationToken.None);

        await cache.WriteAsync(Url, Envelope(40, seed: 2), CancellationToken.None);

        (await cache.TryReadAsync(Url, CancellationToken.None)).Should().Equal(Envelope(40, seed: 2));
    }

    [Theory]
    [InlineData(0)] // magic
    [InlineData(4)] // cache format version
    [InlineData(8)] // sample rate
    [InlineData(12)] // frames per second
    [InlineData(16)] // envelope formula version
    [InlineData(20)] // template seconds
    public async Task Read_ShouldMiss_WhenAHeaderFieldDiffers(int offset)
    {
        var cache = new TemplateCache(_dir);
        await cache.WriteAsync(Url, Envelope(100), CancellationToken.None);

        await Patch(bytes => bytes[offset] ^= 0x01);

        (await cache.TryReadAsync(Url, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Read_ShouldMiss_WhenTheRecordIsForAnotherUrl()
    {
        var cache = new TemplateCache(_dir);
        await cache.WriteAsync(Url, Envelope(100), CancellationToken.None);
        var other = Url.Replace("5so", "6so", StringComparison.Ordinal);
        File.Move(PathFor(Url), PathFor(other));

        (await cache.TryReadAsync(other, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Read_ShouldMiss_WhenASampleIsNotFinite()
    {
        var cache = new TemplateCache(_dir);
        await cache.WriteAsync(Url, Envelope(100), CancellationToken.None);

        await Patch(bytes => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(bytes.Length - 8), float.NaN));

        (await cache.TryReadAsync(Url, CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-4)]
    [InlineData(4)]
    public async Task Read_ShouldMiss_WhenTheLengthDoesNotMatchTheFrameCount(int delta)
    {
        var cache = new TemplateCache(_dir);
        await cache.WriteAsync(Url, Envelope(100), CancellationToken.None);
        var path = PathFor(Url);
        var bytes = await File.ReadAllBytesAsync(path);

        await File.WriteAllBytesAsync(path, delta < 0 ? bytes[..^-delta] : [.. bytes, .. new byte[delta]]);

        (await cache.TryReadAsync(Url, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Read_ShouldMiss_ForAnEmptyOrTinyFile()
    {
        var cache = new TemplateCache(_dir);
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(PathFor(Url), [1, 2, 3]);

        (await cache.TryReadAsync(Url, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Write_ShouldRefuseNonFiniteOrEmptyEnvelopes()
    {
        var cache = new TemplateCache(_dir);

        await cache.Invoking(c => c.WriteAsync(Url, [1f, float.PositiveInfinity], CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
        await cache.Invoking(c => c.WriteAsync(Url, [], CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
        Directory.Exists(_dir).Should().BeFalse("nothing was written");
    }

    private static float[] Envelope(int frames, int seed = 1)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, frames).Select(_ => (float)(rng.NextDouble() * 3)).ToArray();
    }

    private string PathFor(string url) =>
        Path.Combine(_dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".bin");

    private async Task Patch(Action<byte[]> change)
    {
        var path = PathFor(Url);
        var bytes = await File.ReadAllBytesAsync(path);
        change(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
