using System;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>
/// When to try an item again (plan §4.4 and §4.6).
/// </summary>
public static class RetrySchedule
{
    /// <summary>Failed attempts before an item goes to review.</summary>
    public const int MaxFailedAttempts = 5;

    private static readonly TimeSpan[] FirstPendingGaps = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24)];
    private static readonly TimeSpan[] FailedGaps = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromHours(24)];

    /// <summary>
    /// Next check for a show phish.in doesn't have yet: 1 h, 6 h, 24 h, then daily for 14 days, then weekly
    /// up to 60 days.
    /// </summary>
    /// <param name="firstPendingUtc">When the item first went Pending.</param>
    /// <param name="attempts">Checks made so far, including the one just made.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns>The next check, or null to give up (Skipped, no audio).</returns>
    public static DateTime? NextPendingAttempt(DateTime firstPendingUtc, int attempts, DateTime nowUtc)
    {
        var waited = nowUtc - firstPendingUtc;
        if (waited >= TimeSpan.FromDays(60))
        {
            return null;
        }

        if (attempts >= 1 && attempts <= FirstPendingGaps.Length)
        {
            return nowUtc + FirstPendingGaps[attempts - 1];
        }

        return nowUtc + (waited < TimeSpan.FromDays(14) ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Next try after an error.
    /// </summary>
    /// <param name="attempts">Failed attempts so far, including the one just made.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns>The next try, or null to send the item to review.</returns>
    public static DateTime? NextFailedAttempt(int attempts, DateTime nowUtc)
    {
        if (attempts >= MaxFailedAttempts)
        {
            return null;
        }

        return nowUtc + FailedGaps[Math.Clamp(attempts, 1, FailedGaps.Length) - 1];
    }
}
