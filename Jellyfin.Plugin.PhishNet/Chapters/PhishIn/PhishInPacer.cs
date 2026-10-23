using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.PhishNet.Chapters.PhishIn;

/// <summary>
/// Spaces requests to phish.in, a volunteer-run site, one at a time and at least
/// <c>minimumGap</c> apart plus jitter. Register one instance per process.
/// </summary>
public class PhishInPacer
{
    private readonly TimeSpan _minimumGap;
    private readonly TimeSpan _maxJitter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _last;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhishInPacer"/> class with the default spacing
    /// (1 s plus up to 250 ms of jitter).
    /// </summary>
    public PhishInPacer()
        : this(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PhishInPacer"/> class.
    /// </summary>
    /// <param name="minimumGap">Least time between the starts of two requests.</param>
    /// <param name="maxJitter">Random extra wait added to each gap.</param>
    protected PhishInPacer(TimeSpan minimumGap, TimeSpan maxJitter)
    {
        _minimumGap = minimumGap;
        _maxJitter = maxJitter;
    }

    /// <summary>Gets the current time.</summary>
    protected virtual DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <summary>
    /// Waits for this caller's turn and the gap since the previous request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A handle that ends the turn when disposed, after the request has started.</returns>
    public async Task<IDisposable> WaitTurnAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_last is { } last)
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * _maxJitter.TotalMilliseconds);
                var wait = last + _minimumGap + jitter - Now;
                if (wait > TimeSpan.Zero)
                {
                    await DelayAsync(wait, cancellationToken).ConfigureAwait(false);
                }
            }

            _last = Now;
            return new Turn(_gate);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    /// <summary>
    /// Waits for <paramref name="delay"/>.
    /// </summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes after the delay.</returns>
    protected virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

    private sealed class Turn(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
