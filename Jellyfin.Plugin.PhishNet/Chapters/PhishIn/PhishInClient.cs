using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.PhishNet.Chapters.PhishIn;

/// <summary>How usable a phish.in show is for alignment.</summary>
public enum PhishInShowStatus
{
    /// <summary>The show has complete audio and usable tracks.</summary>
    Ok,

    /// <summary>phish.in doesn't have the show (yet); retry later.</summary>
    NotFound,

    /// <summary>The show exists but its audio isn't complete.</summary>
    NoAudio,

    /// <summary>The show's track list can't build a timeline; needs review.</summary>
    Invalid,
}

/// <summary>
/// One phish.in track.
/// </summary>
/// <param name="Title">Song title.</param>
/// <param name="Position">Position in the show (1-based).</param>
/// <param name="SetName">Set name, e.g. "Set 1" or "Encore".</param>
/// <param name="DurationSeconds">Track length.</param>
/// <param name="Mp3Url">Track audio, or null when phish.in has none.</param>
public sealed record PhishInTrack(string Title, int Position, string? SetName, double DurationSeconds, string? Mp3Url);

/// <summary>
/// A phish.in show, reduced to what alignment needs.
/// </summary>
/// <param name="Status">Whether the show is usable.</param>
/// <param name="Detail">Why it isn't, worded as the NAS fleet log words it; null when usable.</param>
/// <param name="Tracks">Non-soundcheck tracks in position order; empty unless usable.</param>
public sealed record PhishInShow(PhishInShowStatus Status, string? Detail, IReadOnlyList<PhishInTrack> Tracks)
{
    /// <summary>Gets the summed track length in seconds.</summary>
    public double TotalSeconds => Tracks.Sum(t => t.DurationSeconds);
}

/// <summary>
/// phish.in asked us to slow down (HTTP 429 or 503).
/// </summary>
public sealed class PhishInRateLimitedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhishInRateLimitedException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="retryAfter">How long phish.in asked us to wait, if it said.</param>
    public PhishInRateLimitedException(HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base($"phish.in returned {(int)statusCode}; retry after {retryAfter?.ToString() ?? "unspecified"}")
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    /// <summary>Gets the HTTP status.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Gets how long phish.in asked us to wait, if it said.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// An open phish.in track download. It holds the pacer's turn, so no other phish.in request starts
/// until it is disposed.
/// </summary>
public sealed class PhishInAudio : IDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly IDisposable _turn;

    internal PhishInAudio(HttpResponseMessage response, Stream body, IDisposable turn)
    {
        _response = response;
        _turn = turn;
        Body = body;
    }

    /// <summary>Gets the MP3 bytes, read as they arrive.</summary>
    public Stream Body { get; }

    /// <summary>Gets the length phish.in reported, if any.</summary>
    public long? ContentLength => _response.Content.Headers.ContentLength;

    /// <summary>Stops the download and frees the pacer for the next request.</summary>
    public void Dispose()
    {
        Body.Dispose();
        _response.Dispose();
        _turn.Dispose();
    }
}

/// <summary>
/// Reads show track lists and track audio from the phish.in API v2. No API key is needed; requests
/// identify the plugin in the User-Agent and go through the shared <see cref="PhishInPacer"/>.
/// Redirects are followed here, at most <see cref="MaxRedirects"/> and only to https phish.in URLs,
/// so the <see cref="HttpClient"/>'s handler must have automatic redirects turned off.
/// </summary>
public sealed class PhishInClient
{
    /// <summary>The phish.in host; track audio is only fetched from here.</summary>
    public const string Host = "phish.in";

    /// <summary>Most redirects followed for one request.</summary>
    public const int MaxRedirects = 3;

    private const string ApiBase = "https://phish.in/api/v2/";

    private readonly HttpClient _http;
    private readonly PhishInPacer _pacer;
    private readonly string _userAgent;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhishInClient"/> class.
    /// </summary>
    /// <param name="http">HTTP client whose handler doesn't follow redirects itself.</param>
    /// <param name="pacer">The process-wide pacer.</param>
    /// <param name="pluginVersion">Plugin version for the User-Agent.</param>
    public PhishInClient(HttpClient http, PhishInPacer pacer, string pluginVersion)
    {
        _http = http;
        _pacer = pacer;
        _userAgent = $"Jellyfin-PhishData/{pluginVersion} (+https://github.com/murphy52/jellyfin-plugin-phishdata)";
    }

    /// <summary>
    /// Fetches a show's track list.
    /// </summary>
    /// <param name="showDate">The show date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The show, or a status saying why it can't be used.</returns>
    public async Task<PhishInShow> GetShowAsync(DateTime showDate, CancellationToken cancellationToken)
    {
        var url = new Uri(ApiBase + "shows/" + showDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var (response, turn) = await SendAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        using var ownedTurn = turn;
        using var ownedResponse = response;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Unusable(PhishInShowStatus.NotFound, "not-on-phishin");
        }

        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(doc.RootElement);
    }

    /// <summary>
    /// Opens a track's MP3 for streaming. The caller reads only what it needs and disposes the result,
    /// which also ends this request's pacer turn.
    /// </summary>
    /// <param name="mp3Url">A phish.in track URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download, headers read and body not yet fetched.</returns>
    public async Task<PhishInAudio> OpenMp3Async(string mp3Url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(mp3Url, UriKind.Absolute, out var uri) || !IsPhishIn(uri))
        {
            throw new ArgumentException($"Not a phish.in track URL: {mp3Url}", nameof(mp3Url));
        }

        var (response, turn) = await SendAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new PhishInAudio(response, body, turn);
        }
        catch
        {
            response.Dispose();
            turn.Dispose();
            throw;
        }
    }

    private static PhishInShow Parse(JsonElement root)
    {
        var audio = root.TryGetProperty("audio_status", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (audio != "complete")
        {
            return Unusable(PhishInShowStatus.NoAudio, $"no-audio ({audio ?? "unknown"})");
        }

        var tracks = new List<PhishInTrack>();
        var positions = new HashSet<int>();
        if (root.TryGetProperty("tracks", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in list.EnumerateArray())
            {
                var title = GetString(t, "title") ?? string.Empty;
                var set = GetString(t, "set_name");
                if (set == "Soundcheck" || title.StartsWith("(Check)", StringComparison.Ordinal))
                {
                    continue; // not in the webcast, and poisons the alignment
                }

                int position = t.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pos) ? pos : 0;
                double ms = t.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                if (string.IsNullOrWhiteSpace(title) || !double.IsFinite(ms) || ms <= 0)
                {
                    // Every duration feeds the cumulative timeline; guessing one would shift every later song.
                    return Unusable(PhishInShowStatus.Invalid, $"track {position} '{title}' has no duration or title");
                }

                if (position <= 0 || !positions.Add(position))
                {
                    // The order builds the timeline too. Gaps are fine (soundchecks were dropped).
                    return Unusable(PhishInShowStatus.Invalid, $"track '{title}' has a missing or repeated position {position}");
                }

                tracks.Add(new PhishInTrack(title, position, set, ms / 1000.0, GetString(t, "mp3_url")));
            }
        }

        return tracks.Count == 0
            ? Unusable(PhishInShowStatus.Invalid, "no-songs")
            : new PhishInShow(PhishInShowStatus.Ok, null, tracks.OrderBy(t => t.Position).ToList());
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static PhishInShow Unusable(PhishInShowStatus status, string detail) => new(status, detail, Array.Empty<PhishInTrack>());

    private static bool IsPhishIn(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);

    private static bool IsRedirect(HttpStatusCode code) => code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Sends a GET in one pacer turn, following up to <see cref="MaxRedirects"/> phish.in redirects.
    /// The caller disposes both the response and the turn.
    /// </summary>
    private async Task<(HttpResponseMessage Response, IDisposable Turn)> SendAsync(Uri url, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        var turn = await _pacer.WaitTurnAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(_userAgent);
                var response = await _http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    response.Dispose();
                    var next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(url, location);
                    if (next is null || !IsPhishIn(next) || redirects == MaxRedirects)
                    {
                        throw new HttpRequestException($"phish.in redirect from {url} to {location?.ToString() ?? "nowhere"} not followed");
                    }

                    url = next;
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var retryAfter = response.Headers.RetryAfter is { } h
                        ? h.Delta ?? (h.Date is { } date ? date - DateTimeOffset.UtcNow : null)
                        : null;
                    response.Dispose();
                    throw new PhishInRateLimitedException(response.StatusCode, retryAfter);
                }

                return (response, turn);
            }
        }
        catch
        {
            turn.Dispose();
            throw;
        }
    }
}
