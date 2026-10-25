using System.Net;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// The template source turns a phish.in show into one envelope per track: from the cache when it can,
/// otherwise by streaming the MP3 through the decoder under the per-track and per-show byte caps.
/// Only complete templates are kept and cached.
/// </summary>
public class PhishInTemplateSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phishin-source-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTemplates_ShouldDecodeAndCacheEachTrack_AndLeaveTracksWithoutAudioNull()
    {
        var (source, handler, decoder, cache) = Create();
        var show = Show(Track("Ghost", 1, "https://phish.in/blob/a.mp3"), Track("Chalk Dust Torture", 2, null), Track("Tweezer", 3, "https://phish.in/blob/c.mp3"));

        var result = await source.GetTemplatesAsync(show, CancellationToken.None);

        result.Templates.Should().HaveCount(3);
        result.Templates[0].Should().Equal(decoder.EnvelopeFor("https://phish.in/blob/a.mp3"));
        result.Templates[1].Should().BeNull();
        result.Templates[2].Should().Equal(decoder.EnvelopeFor("https://phish.in/blob/c.mp3"));
        result.Downloaded.Should().Be(2);
        result.CacheHits.Should().Be(0);
        result.BytesRead.Should().Be(2 * FakeDecoder.BytesPerTrack);
        handler.Requests.Should().HaveCount(2);
        (await cache.TryReadAsync("https://phish.in/blob/a.mp3", CancellationToken.None)).Should().Equal(result.Templates[0]);
    }

    [Fact]
    public async Task GetTemplates_ShouldNotDownload_WhenTheTemplateIsCached()
    {
        var (source, handler, _, cache) = Create();
        var cached = Enumerable.Range(0, AlignmentConstants.TemplateFrames).Select(i => (float)i).ToArray();
        await cache.WriteAsync("https://phish.in/blob/a.mp3", cached, CancellationToken.None);

        var result = await source.GetTemplatesAsync(Show(Track("Ghost", 1, "https://phish.in/blob/a.mp3")), CancellationToken.None);

        result.Templates[0].Should().Equal(cached);
        result.CacheHits.Should().Be(1);
        result.Downloaded.Should().Be(0);
        result.BytesRead.Should().Be(0);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTemplates_ShouldDropAndNotCache_ATemplateCutShortByTheByteCap()
    {
        var (source, _, decoder, cache) = Create();
        decoder.Capped.Add("https://phish.in/blob/a.mp3");

        var result = await source.GetTemplatesAsync(Show(Track("Ghost", 1, "https://phish.in/blob/a.mp3")), CancellationToken.None);

        result.Templates[0].Should().BeNull();
        result.Incomplete.Should().Be(1);
        result.BytesRead.Should().Be(FakeDecoder.BytesPerTrack);
        (await cache.TryReadAsync("https://phish.in/blob/a.mp3", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetTemplates_ShouldKeepAShortTrack_WhoseWholeLengthWasDecoded()
    {
        var (source, _, decoder, _) = Create();
        decoder.Frames["https://phish.in/blob/a.mp3"] = 40 * AlignmentConstants.Fps;

        var result = await source.GetTemplatesAsync(Show(Track("Contact", 1, "https://phish.in/blob/a.mp3", seconds: 40)), CancellationToken.None);

        result.Templates[0].Should().HaveCount(40 * AlignmentConstants.Fps);
    }

    [Fact]
    public async Task GetTemplates_ShouldPassTheSmallerOfTheTrackCapAndWhatIsLeftOfTheShowBudget()
    {
        var (source, _, decoder, _) = Create(showBudget: (2 * EnvelopeDecoder.MaxTrackBytes) + 100);
        decoder.BytesOverride = EnvelopeDecoder.MaxTrackBytes;
        var show = Show(Track("A", 1, "https://phish.in/blob/a.mp3"), Track("B", 2, "https://phish.in/blob/b.mp3"), Track("C", 3, "https://phish.in/blob/c.mp3"), Track("D", 4, "https://phish.in/blob/d.mp3"));

        var result = await source.GetTemplatesAsync(show, CancellationToken.None);

        decoder.MaxBytesSeen.Should().Equal(EnvelopeDecoder.MaxTrackBytes, EnvelopeDecoder.MaxTrackBytes, 100);
        result.Templates[3].Should().BeNull("the show budget was used up, so the track wasn't fetched");
        result.OverBudget.Should().Be(1);
    }

    [Fact]
    public async Task GetTemplates_ShouldLetARateLimitThrough()
    {
        var (source, _, _, _) = Create(respond: _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var act = () => source.GetTemplatesAsync(Show(Track("Ghost", 1, "https://phish.in/blob/a.mp3")), CancellationToken.None);

        await act.Should().ThrowAsync<PhishInRateLimitedException>();
    }

    [Fact]
    public async Task GetTemplates_ShouldKeepTheTemplatesAlreadyCached_WhenALaterTrackFails()
    {
        var (source, _, _, cache) = Create(respond: r => r.RequestUri!.AbsolutePath.EndsWith("b.mp3", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Mp3(r));

        var act = () => source.GetTemplatesAsync(Show(Track("A", 1, "https://phish.in/blob/a.mp3"), Track("B", 2, "https://phish.in/blob/b.mp3")), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        (await cache.TryReadAsync("https://phish.in/blob/a.mp3", CancellationToken.None)).Should().NotBeNull("a retry shouldn't download it again");
    }

    [Fact]
    public async Task GetTemplates_ShouldStop_WhenCancelled()
    {
        var (source, handler, _, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => source.GetTemplatesAsync(Show(Track("A", 1, "https://phish.in/blob/a.mp3")), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    private static PhishInTrack Track(string title, int position, string? url, double seconds = 300) => new(title, position, "Set 1", seconds, url);

    private static PhishInShow Show(params PhishInTrack[] tracks) => new(PhishInShowStatus.Ok, null, tracks);

    /// <summary>A stand-in MP3 whose bytes are its own URL, so the fake decoder knows which track it got.</summary>
    private static HttpResponseMessage Mp3(HttpRequestMessage request) =>
        new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.ToString()) };

    private (PhishInTemplateSource Source, FakeHandler Handler, FakeDecoder Decoder, TemplateCache Cache) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        long showBudget = EnvelopeDecoder.MaxShowBytes)
    {
        var handler = new FakeHandler(respond ?? Mp3);
        var client = new PhishInClient(new HttpClient(handler), new InstantPacer(), "1.6.1.0");
        var decoder = new FakeDecoder();
        var cache = new TemplateCache(_dir);
        return (new PhishInTemplateSource(client, decoder, cache) { ShowByteBudget = showBudget }, handler, decoder, cache);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class InstantPacer() : PhishInPacer(TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>Returns a per-URL envelope, reading the URL from the stand-in MP3.</summary>
    private sealed class FakeDecoder : IEnvelopeDecoder
    {
        public const int BytesPerTrack = 1000;

        public Dictionary<string, int> Frames { get; } = new();

        public HashSet<string> Capped { get; } = new();

        public List<long> MaxBytesSeen { get; } = new();

        public long? BytesOverride { get; set; }

        public float[] EnvelopeFor(string url) =>
            Enumerable.Range(0, Frames.GetValueOrDefault(url, AlignmentConstants.TemplateFrames)).Select(i => (float)(url.Length + i)).ToArray();

        public Task<float[]> DecodeFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<TemplateDecodeResult> DecodeTemplateAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
        {
            MaxBytesSeen.Add(maxBytes);
            using var reader = new StreamReader(source, leaveOpen: true);
            var url = await reader.ReadToEndAsync(cancellationToken);
            return new TemplateDecodeResult(EnvelopeFor(url), BytesOverride ?? BytesPerTrack, Capped.Contains(url));
        }
    }
}
