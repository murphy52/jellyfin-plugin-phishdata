namespace Jellyfin.Plugin.PhishNet.Tests.Chapters;

/// <summary>A clock the test sets by hand.</summary>
internal sealed class TestClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => new(Now);
}
