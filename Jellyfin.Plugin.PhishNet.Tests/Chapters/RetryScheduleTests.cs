using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>
/// Shows phish.in doesn't have yet are retried at 1 h, 6 h, 24 h, then daily for 14 days, then weekly
/// up to 60 days. Failures back off and go to review after 5 attempts.
/// </summary>
public class RetryScheduleTests
{
    private static readonly DateTime First = new(2026, 10, 1, 4, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 6)]
    [InlineData(3, 0, 24)]
    [InlineData(4, 2, 24)]
    [InlineData(10, 13, 24)]
    [InlineData(11, 14, 24 * 7)]
    [InlineData(20, 59, 24 * 7)]
    public void Pending_ShouldBackOff(int attempts, int daysSinceFirst, int expectedHours)
    {
        var now = First.AddDays(daysSinceFirst);

        RetrySchedule.NextPendingAttempt(First, attempts, now).Should().Be(now.AddHours(expectedHours));
    }

    [Fact]
    public void Pending_ShouldGiveUpAfterSixtyDays()
    {
        RetrySchedule.NextPendingAttempt(First, 25, First.AddDays(60)).Should().BeNull();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 6)]
    [InlineData(3, 24)]
    [InlineData(4, 24)]
    public void Failed_ShouldBackOff(int attempts, int expectedHours)
    {
        RetrySchedule.NextFailedAttempt(attempts, First).Should().Be(First.AddHours(expectedHours));
    }

    [Fact]
    public void Failed_ShouldGoToReviewAfterFiveAttempts()
    {
        RetrySchedule.NextFailedAttempt(RetrySchedule.MaxFailedAttempts, First).Should().BeNull();
    }
}
