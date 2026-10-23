namespace Jellyfin.Plugin.PhishNet.Chapters.Alignment;

/// <summary>
/// Tuning shared with the NAS prior art (Ugreen_NAS/scripts/phish_core.py). Keep these identical to it
/// until a change is proven on the fixture corpus; the golden parity test depends on them.
/// </summary>
public static class AlignmentConstants
{
    /// <summary>PCM sample rate the audio is decoded to, in Hz.</summary>
    public const int SampleRate = 8000;

    /// <summary>Envelope frames per second (50 ms hops).</summary>
    public const int Fps = 20;

    /// <summary>Seconds of each track's audio used as its template.</summary>
    public const int TemplateSeconds = 90;

    /// <summary>Minimum gap between consecutive songs, in seconds.</summary>
    public const int MinGapSeconds = 5;

    /// <summary>A global match at or above this NCC counts as an anchor for the line fit.</summary>
    public const double AnchorConfidence = 0.45;

    /// <summary>Anchors must lie within this many seconds of the fitted line to agree.</summary>
    public const double AgreementSeconds = 90;

    /// <summary>Lowest drift slope the fit may use.</summary>
    public const double SlopeMin = 0.85;

    /// <summary>Highest drift slope the fit may use.</summary>
    public const double SlopeMax = 1.25;

    /// <summary>Half-width, in seconds, of the per-song refine window around the fitted time.</summary>
    public const int RefineWindowSeconds = 150;

    /// <summary>A refine match below this NCC falls back to the fitted time.</summary>
    public const double RefineAcceptConfidence = 0.30;

    /// <summary>A song whose NCC is below this counts as low-confidence.</summary>
    public const double LowConfidence = 0.35;

    /// <summary>A show whose median NCC is below this goes to review instead of being applied.</summary>
    public const double MinMedianConfidence = 0.40;

    /// <summary>Lowest accepted ratio of video length to phish.in show length.</summary>
    public const double DurationRatioLow = 0.60;

    /// <summary>Highest accepted ratio of video length to phish.in show length.</summary>
    public const double DurationRatioHigh = 1.50;

    /// <summary>Videos shorter than this, in seconds, are clips rather than shows.</summary>
    public const double MinVideoSeconds = 600;

    /// <summary>Gets the template length in envelope frames.</summary>
    public const int TemplateFrames = TemplateSeconds * Fps;
}
