using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>What <see cref="SidecarChapterReader"/> found next to a video.</summary>
public enum SidecarStatus
{
    /// <summary>No sidecar.</summary>
    Missing,

    /// <summary>Every line parsed.</summary>
    Ok,

    /// <summary>The file exists but can't be used; <see cref="SidecarResult.Error"/> says why.</summary>
    Invalid,
}

/// <summary>
/// A sidecar's contents.
/// </summary>
/// <param name="Status">What was found.</param>
/// <param name="Hash">Hash of the file's bytes, part of the item's fingerprint; null when there is no file.</param>
/// <param name="Chapters">The chapters, in file order; empty unless <see cref="SidecarStatus.Ok"/>.</param>
/// <param name="Error">Why the file was rejected, starting with the line number when one line is at fault.</param>
public sealed record SidecarResult(SidecarStatus Status, string? Hash, IReadOnlyList<AlignedChapter> Chapters, string? Error)
{
    /// <summary>Gets the result for a video with no sidecar.</summary>
    public static SidecarResult Missing { get; } = new(SidecarStatus.Missing, null, Array.Empty<AlignedChapter>(), null);
}

/// <summary>
/// Reads the manual override (plan §4.4): <c>&lt;video basename&gt;.chapters.txt</c> next to the video, one
/// <c>H:MM:SS Title</c> (or <c>M:SS Title</c>, ASCII digits) per line, with any line ending. Blank lines and lines starting with <c>#</c> are
/// skipped. The whole file must parse, or none of it is used. Starts past the end of the video are left to
/// <see cref="ChapterValidator"/>, which knows the runtime.
/// </summary>
public static partial class SidecarChapterReader
{
    /// <summary>What follows the video's base name.</summary>
    public const string Suffix = ".chapters.txt";

    /// <summary>The largest sidecar read; a chapter list is a few KB.</summary>
    public const int MaxBytes = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Gets where a video's sidecar lives.
    /// </summary>
    /// <param name="videoPath">The video file.</param>
    /// <returns>The sidecar's path.</returns>
    public static string PathFor(string videoPath) =>
        Path.Combine(Path.GetDirectoryName(videoPath) ?? string.Empty, Path.GetFileNameWithoutExtension(videoPath) + Suffix);

    /// <summary>
    /// Reads a video's sidecar, if it has one. I/O errors other than a missing file are thrown.
    /// </summary>
    /// <param name="videoPath">The video file.</param>
    /// <returns>What was found.</returns>
    public static SidecarResult Read(string videoPath)
    {
        var info = new FileInfo(PathFor(videoPath));
        if (!info.Exists)
        {
            return SidecarResult.Missing;
        }

        // Not read, so the size and time stand in for the content hash.
        var stat = string.Create(CultureInfo.InvariantCulture, $"stat-{info.Length}-{info.LastWriteTimeUtc.Ticks}");

        // A link's size is the link's, and its target could be anything Jellyfin can read (/dev/zero, a
        // secret), so links aren't followed.
        if (info.LinkTarget is not null)
        {
            return Invalid(stat, "sidecar is a link; put a regular file there");
        }

        // A FIFO or device also reports 0 bytes, and opening one can block; an empty file has no chapters anyway.
        if (info.Length == 0)
        {
            return Invalid(stat, "sidecar has no chapters");
        }

        if (info.Length > MaxBytes)
        {
            return Invalid(stat, $"sidecar is larger than {MaxBytes / 1024} KB");
        }

        byte[] bytes;
        try
        {
            bytes = ReadAtMost(info.FullName, MaxBytes + 1);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return SidecarResult.Missing; // removed between the stat and the read
        }

        return bytes.Length > MaxBytes ? Invalid(stat, $"sidecar is larger than {MaxBytes / 1024} KB") : Parse(bytes);
    }

    /// <summary>Reads up to <paramref name="limit"/> bytes, so a file that grows after the stat can't make the read unbounded.</summary>
    private static byte[] ReadAtMost(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[limit];
        int total = 0;
        int read;
        while (total < limit && (read = stream.Read(buffer, total, limit - total)) > 0)
        {
            total += read;
        }

        return buffer.AsSpan(0, total).ToArray();
    }

    /// <summary>
    /// Parses a sidecar's bytes.
    /// </summary>
    /// <param name="bytes">The file's contents.</param>
    /// <returns>The chapters, or why they can't be used.</returns>
    public static SidecarResult Parse(byte[] bytes)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Invalid(hash, "sidecar is not UTF-8 text");
        }

        var chapters = new List<AlignedChapter>();
        var lines = text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int number = i + 1;
            var match = LinePattern().Match(line);
            if (!match.Success || ParseStart(match) is not { } start)
            {
                return Invalid(hash, $"sidecar line {number}: expected 'H:MM:SS Title', got '{Clip(line)}'");
            }

            var title = match.Groups["title"].Value.Trim();
            if (title.Length > ChapterValidator.MaxTitleLength)
            {
                return Invalid(hash, $"sidecar line {number}: title is longer than {ChapterValidator.MaxTitleLength} characters");
            }

            if (chapters.Count > 0 && ChapterDetector.ToTicks(start) <= ChapterDetector.ToTicks(chapters[^1].StartSeconds))
            {
                return Invalid(hash, $"sidecar line {number}: {AlignmentGates.Hms(start)} is not after the chapter before it");
            }

            if (chapters.Count == ChapterValidator.MaxChapters)
            {
                return Invalid(hash, $"sidecar line {number}: more than {ChapterValidator.MaxChapters} chapters");
            }

            chapters.Add(new AlignedChapter(title, start, null));
        }

        return chapters.Count == 0
            ? Invalid(hash, "sidecar has no chapters")
            : new SidecarResult(SidecarStatus.Ok, hash, chapters, null);
    }

    private static double? ParseStart(Match match)
    {
        int hours = match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        int minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        if (seconds > 59 || (match.Groups["h"].Success && minutes > 59))
        {
            return null;
        }

        double fraction = match.Groups["f"].Success ? double.Parse("0." + match.Groups["f"].Value, CultureInfo.InvariantCulture) : 0;
        return (hours * 3600) + (minutes * 60) + seconds + fraction;
    }

    private static SidecarResult Invalid(string hash, string error) => new(SidecarStatus.Invalid, hash, Array.Empty<AlignedChapter>(), error);

    private static string Clip(string line) => line.Length <= 60 ? line : line[..60] + "…";

    [GeneratedRegex(@"^(?:(?<h>[0-9]{1,2}):)?(?<m>[0-9]{1,2}):(?<s>[0-9]{2})(?:\.(?<f>[0-9]{1,3}))?\s+(?<title>\S.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinePattern();
}
