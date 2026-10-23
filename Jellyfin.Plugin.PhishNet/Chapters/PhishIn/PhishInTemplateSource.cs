using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;

namespace Jellyfin.Plugin.PhishNet.Chapters.PhishIn;

/// <summary>
/// One template per track of a show, plus what getting them cost.
/// </summary>
/// <param name="Templates">Per track in show order: the envelope, or null when the track has no usable audio.</param>
/// <param name="CacheHits">Templates read from the cache.</param>
/// <param name="Downloaded">Templates fetched and decoded.</param>
/// <param name="Incomplete">Fetched templates dropped because the byte cap cut them short.</param>
/// <param name="OverBudget">Tracks not fetched because the show's byte budget ran out.</param>
/// <param name="BytesRead">MP3 bytes read from phish.in.</param>
public sealed record ShowTemplates(IReadOnlyList<float[]?> Templates, int CacheHits, int Downloaded, int Incomplete, int OverBudget, long BytesRead);

/// <summary>
/// Where a show's track list and track audio come from. An interface so the chapter service can be
/// tested without phish.in or ffmpeg.
/// </summary>
public interface IShowAudioSource
{
    /// <summary>
    /// Fetches a show's track list.
    /// </summary>
    /// <param name="showDate">The show date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The show, or a status saying why it can't be used.</returns>
    Task<PhishInShow> GetShowAsync(DateTime showDate, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a template for every track of a usable show.
    /// </summary>
    /// <param name="show">A show whose status is <see cref="PhishInShowStatus.Ok"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The templates, one per track.</returns>
    Task<ShowTemplates> GetTemplatesAsync(PhishInShow show, CancellationToken cancellationToken);
}

/// <summary>
/// phish.in as an <see cref="IShowAudioSource"/>: the cache first, then the first
/// 90 s of each MP3 streamed through ffmpeg. Errors other than "no audio for this track" propagate, so a
/// network fault fails the show (and it's retried) rather than quietly aligning with fewer anchors; the
/// templates fetched before the fault stay cached.
/// </summary>
public sealed class PhishInTemplateSource : IShowAudioSource
{
    private readonly PhishInClient _client;
    private readonly IEnvelopeDecoder _decoder;
    private readonly TemplateCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhishInTemplateSource"/> class.
    /// </summary>
    /// <param name="client">The phish.in client.</param>
    /// <param name="decoder">The ffmpeg decoder.</param>
    /// <param name="cache">The template cache.</param>
    public PhishInTemplateSource(PhishInClient client, IEnvelopeDecoder decoder, TemplateCache cache)
    {
        _client = client;
        _decoder = decoder;
        _cache = cache;
    }

    /// <summary>Gets the most MP3 bytes read for one show.</summary>
    public long ShowByteBudget { get; init; } = EnvelopeDecoder.MaxShowBytes;

    /// <inheritdoc />
    public Task<PhishInShow> GetShowAsync(DateTime showDate, CancellationToken cancellationToken) =>
        _client.GetShowAsync(showDate, cancellationToken);

    /// <inheritdoc />
    public async Task<ShowTemplates> GetTemplatesAsync(PhishInShow show, CancellationToken cancellationToken)
    {
        var templates = new float[]?[show.Tracks.Count];
        int hits = 0, downloaded = 0, incomplete = 0, overBudget = 0;
        long bytes = 0;
        for (int i = 0; i < show.Tracks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = show.Tracks[i];
            if (track.Mp3Url is not { } url)
            {
                continue; // the aligner fits a chapter for it from the line
            }

            if (await _cache.TryReadAsync(url, cancellationToken).ConfigureAwait(false) is { } cached)
            {
                templates[i] = cached;
                hits++;
                continue;
            }

            long remaining = ShowByteBudget - bytes;
            if (remaining <= 0)
            {
                overBudget++;
                continue;
            }

            TemplateDecodeResult result;
            using (var audio = await _client.OpenMp3Async(url, cancellationToken).ConfigureAwait(false))
            {
                result = await _decoder.DecodeTemplateAsync(audio.Body, Math.Min(EnvelopeDecoder.MaxTrackBytes, remaining), cancellationToken).ConfigureAwait(false);
            }

            bytes += result.BytesRead;
            downloaded++;
            if (!result.IsCompleteFor(track.DurationSeconds))
            {
                incomplete++;
                continue;
            }

            templates[i] = result.Envelope;
            await _cache.WriteAsync(url, result.Envelope, cancellationToken).ConfigureAwait(false);
        }

        return new ShowTemplates(templates, hits, downloaded, incomplete, overBudget, bytes);
    }
}
