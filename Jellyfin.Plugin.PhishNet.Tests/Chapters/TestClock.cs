namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>A clock the test sets by hand.</summary>
internal sealed class TestClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;

    /// <summary>Gets or sets the server's time zone; UTC unless a test says otherwise.</summary>
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public override TimeZoneInfo LocalTimeZone => Zone;

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => new(Now);
}
