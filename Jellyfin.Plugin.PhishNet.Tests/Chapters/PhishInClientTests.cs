using System.Net;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// phish.in v2 needs no API key. The client must keep every non-soundcheck track (the durations
/// build the timeline), pace its requests, and turn "not there yet" into a retryable status.
/// </summary>
public class PhishInClientTests
{
    private const string Show = """
        {"date":"2026-07-07","audio_status":"complete","tracks":[
          {"title":"Ghost","position":3,"duration":600000,"set_name":"Set 1","mp3_url":"https://phish.in/blob/b.mp3"},
          {"title":"Soundcheck Jam","position":1,"duration":120000,"set_name":"Soundcheck","mp3_url":"https://phish.in/blob/s.mp3"},
          {"title":"(Check) Funky Bitch","position":2,"duration":90000,"set_name":"Set 1","mp3_url":"https://phish.in/blob/c.mp3"},
          {"title":"Chalk Dust Torture","position":4,"duration":604068,"set_name":"Set 1","mp3_url":null},
          {"title":"Tweezer","position":5,"duration":900000,"set_name":"Set 2","mp3_url":"https://phish.in/blob/t.mp3"}
        ]}
        """;

    [Fact]
    public async Task GetShow_ShouldKeepEveryNonSoundcheckTrackInPositionOrder()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, Show));

        var show = await client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);

        show.Status.Should().Be(PhishInShowStatus.Ok);
        show.Tracks.Select(t => t.Title).Should().Equal("Ghost", "Chalk Dust Torture", "Tweezer");
        show.Tracks[1].Mp3Url.Should().BeNull();
        show.Tracks[0].DurationSeconds.Should().Be(600);
        show.Tracks[2].SetName.Should().Be("Set 2");
        show.TotalSeconds.Should().BeApproximately(2104.068, 1e-6);
    }

    [Fact]
    public async Task GetShow_ShouldRequestTheDateWithAProjectUserAgentAndNoKey()
    {
        var (client, handler, _) = Create(_ => Json(HttpStatusCode.OK, Show));

        await client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);

        var request = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be("https://phish.in/api/v2/shows/2026-07-07");
        request.Headers.UserAgent.ToString().Should().Contain("Jellyfin-PhishData").And.Contain("github.com/murphy52");
        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task GetShow_ShouldReportNotFound_ForAShowPhishInDoesNotHaveYet()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.NotFound, "{}"));

        var show = await client.GetShowAsync(new DateTime(2026, 10, 21), CancellationToken.None);

        show.Status.Should().Be(PhishInShowStatus.NotFound);
        show.Tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetShow_ShouldReportNoAudio_WhenAudioIsIncomplete()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, """{"audio_status":"missing","tracks":[]}"""));

        var show = await client.GetShowAsync(new DateTime(2024, 12, 1), CancellationToken.None);

        show.Status.Should().Be(PhishInShowStatus.NoAudio);
        show.Detail.Should().Be("no-audio (missing)");
    }

    [Fact]
    public async Task GetShow_ShouldReportInvalid_WhenATrackHasNoDuration()
    {
        var json = """{"audio_status":"complete","tracks":[{"title":"Ghost","position":1,"duration":null,"set_name":"Set 1","mp3_url":"https://phish.in/blob/b.mp3"}]}""";
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, json));

        var show = await client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);

        show.Status.Should().Be(PhishInShowStatus.Invalid);
        show.Detail.Should().Contain("Ghost");
    }

    [Fact]
    public async Task GetShow_ShouldReportInvalid_WhenOnlySoundcheckRemains()
    {
        var json = """{"audio_status":"complete","tracks":[{"title":"Jam","position":1,"duration":1000,"set_name":"Soundcheck","mp3_url":"https://phish.in/blob/s.mp3"}]}""";
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, json));

        var show = await client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);

        show.Status.Should().Be(PhishInShowStatus.Invalid);
        show.Detail.Should().Be("no-songs");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetShow_ShouldThrowRateLimited_WithRetryAfter(HttpStatusCode code)
    {
        var (client, _, _) = Create(_ =>
        {
            var r = Json(code, "{}");
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return r;
        });

        var act = () => client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PhishInRateLimitedException>()).Which;
        ex.RetryAfter.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task Requests_ShouldBePacedAtLeastOneSecondApart()
    {
        var (client, _, pacer) = Create(_ => Json(HttpStatusCode.OK, Show));

        await client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);
        using (await client.OpenMp3Async("https://phish.in/blob/b.mp3", CancellationToken.None))
        {
        }

        pacer.Delays.Should().ContainSingle().Which.Should().BeGreaterOrEqualTo(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task OpenMp3_ShouldOnlyFetchPhishInUrls()
    {
        var (client, handler, _) = Create(_ => Json(HttpStatusCode.OK, "{}"));

        var act = () => client.OpenMp3Async("https://example.com/x.mp3", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"title":"Ghost","duration":1000,"set_name":"Set 1"}""", "position 0")]
    [InlineData("""{"title":"Ghost","position":-2,"duration":1000,"set_name":"Set 1"}""", "position -2")]
    [InlineData("""{"title":"Ghost","position":1,"duration":1000,"set_name":"Set 1"}""", "repeated position 1")]
    public async Task GetShow_ShouldReportInvalid_WhenAPositionIsMissingOrRepeated(string track, string detail)
    {
        var json = $$"""{"audio_status":"complete","tracks":[{"title":"Tweezer","position":1,"duration":1000,"set_name":"Set 1"},{{track}}]}""";
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, json));

        var show = await client.GetShowAsync(new DateTime(2026, 7, 7), CancellationToken.None);

        show.Status.Should().Be(PhishInShowStatus.Invalid);
        show.Detail.Should().Contain(detail);
    }

    [Fact]
    public async Task OpenMp3_ShouldHoldThePacerUntilTheDownloadIsDisposed()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.OK, "{}"));
        var first = await client.OpenMp3Async("https://phish.in/blob/a.mp3", CancellationToken.None);

        var second = client.OpenMp3Async("https://phish.in/blob/b.mp3", CancellationToken.None);
        await Task.Delay(100);
        second.IsCompleted.Should().BeFalse("only one phish.in request may run at a time");

        first.Dispose();
        using var audio = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenMp3_ShouldFollowARedirectWithinPhishIn()
    {
        var (client, handler, _) = Create(r => r.RequestUri!.AbsolutePath == "/blob/a.mp3"
            ? Redirect("/blob/moved.mp3")
            : Json(HttpStatusCode.OK, "mp3"));

        using var audio = await client.OpenMp3Async("https://phish.in/blob/a.mp3", CancellationToken.None);

        handler.Requests.Select(r => r.RequestUri!.ToString())
            .Should().Equal("https://phish.in/blob/a.mp3", "https://phish.in/blob/moved.mp3");
        handler.Requests[1].Headers.UserAgent.ToString().Should().Contain("Jellyfin-PhishData");
    }

    [Theory]
    [InlineData("https://example.com/x.mp3")]
    [InlineData("http://phish.in/blob/a.mp3")]
    [InlineData("https://169.254.169.254/latest")]
    public async Task OpenMp3_ShouldNotFollowARedirectOffPhishIn(string location)
    {
        var (client, handler, _) = Create(_ => Redirect(location));

        var act = () => client.OpenMp3Async("https://phish.in/blob/a.mp3", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task OpenMp3_ShouldGiveUpAfterThreeRedirects()
    {
        int n = 0;
        var (client, handler, _) = Create(_ => Redirect($"/blob/hop{++n}.mp3"));

        var act = () => client.OpenMp3Async("https://phish.in/blob/a.mp3", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().HaveCount(PhishInClient.MaxRedirects + 1);
    }

    [Fact]
    public async Task OpenMp3_ShouldFreeThePacer_WhenTheRequestFails()
    {
        var (client, _, _) = Create(_ => Json(HttpStatusCode.NotFound, "{}"));

        var act = () => client.OpenMp3Async("https://phish.in/blob/a.mp3", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        await act.Should().ThrowAsync<HttpRequestException>().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return r;
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (PhishInClient Client, FakeHandler Handler, RecordingPacer Pacer) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var pacer = new RecordingPacer();
        var client = new PhishInClient(new HttpClient(handler), pacer, "1.6.1.0");
        return (client, handler, pacer);
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

    /// <summary>A pacer on a fake clock that records the waits it would have made.</summary>
    private sealed class RecordingPacer : PhishInPacer
    {
        private DateTimeOffset _now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

        public RecordingPacer()
            : base(TimeSpan.FromSeconds(1), TimeSpan.Zero)
        {
        }

        public List<TimeSpan> Delays { get; } = new();

        protected override DateTimeOffset Now => _now;

        protected override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            _now += delay;
            return Task.CompletedTask;
        }
    }
}
