using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Chapters.Alignment;

namespace Jellyfin.Plugin.PhishNet.Chapters;

/// <summary>Where an item stands.</summary>
public enum ChapterStatus
{
    /// <summary>Our chapters are saved in Jellyfin.</summary>
    Applied,

    /// <summary>Other real chapters replaced ours; left alone.</summary>
    Superseded,

    /// <summary>phish.in doesn't have the show yet, or the file was still changing; retried later.</summary>
    Pending,

    /// <summary>Alignment or the show data didn't pass the checks; waits for the admin.</summary>
    NeedsReview,

    /// <summary>An error; retried with backoff.</summary>
    Failed,

    /// <summary>Not a show to chapter (has chapters, too short, a clip, no audio for good).</summary>
    Skipped,

    /// <summary>The admin chose to leave it alone.</summary>
    Ignored,
}

/// <summary>
/// Identifies one version of a media file. A different fingerprint means a new file to align.
/// </summary>
/// <param name="Path">The file path.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="ModifiedUtc">Last write time.</param>
/// <param name="SidecarHash">Hash of the chapters sidecar, when one exists (Phase 4).</param>
public sealed record FileFingerprint(string Path, long Size, DateTime ModifiedUtc, string? SidecarHash = null)
{
    /// <summary>
    /// Stats a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>Its fingerprint, or null when it doesn't exist.</returns>
    public static FileFingerprint? Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileFingerprint(path, info.Length, info.LastWriteTimeUtc) : null;
    }
}

/// <summary>
/// The plugin's record for one item.
/// </summary>
public sealed class ChapterState
{
    /// <summary>Gets or sets the record format.</summary>
    public int SchemaVersion { get; set; } = ChapterStateStore.SchemaVersion;

    /// <summary>Gets or sets the alignment version that produced the record.</summary>
    public int AlgorithmVersion { get; set; }

    /// <summary>Gets or sets the status.</summary>
    public ChapterStatus Status { get; set; }

    /// <summary>Gets or sets why the item has this status.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets the file the record is for.</summary>
    public FileFingerprint? Fingerprint { get; set; }

    /// <summary>Gets or sets the show date parsed from the item.</summary>
    public DateTime? ShowDate { get; set; }

    /// <summary>Gets or sets the chapters: saved ones when Applied, the rejected alignment when NeedsReview.</summary>
    public List<AlignedChapter> Chapters { get; set; } = new();

    /// <summary>Gets or sets the alignment's median confidence.</summary>
    public double? MedianConfidence { get; set; }

    /// <summary>Gets or sets how many songs were low-confidence.</summary>
    public int LowConfidenceCount { get; set; }

    /// <summary>Gets or sets the attempts so far in the current Pending or Failed stretch.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets when the item first went Pending, for the give-up limit.</summary>
    public DateTime? FirstPendingUtc { get; set; }

    /// <summary>Gets or sets the earliest time to try again.</summary>
    public DateTime? NextAttemptUtc { get; set; }

    /// <summary>Gets or sets when the record was written.</summary>
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// Keeps one JSON record per item under <c>&lt;DataPath&gt;/phishdata/chapters/&lt;itemId&gt;.json</c>,
/// a folder that doesn't move when the plugin version changes. Writes are atomic.
/// </summary>
public sealed class ChapterStateStore
{
    /// <summary>The current record format.</summary>
    public const int SchemaVersion = 1;

    private const long MaxRecordBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterStateStore"/> class.
    /// </summary>
    /// <param name="directory">The records folder; created on first write.</param>
    public ChapterStateStore(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Reads an item's record.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or null when there is none or it can't be read (damaged, or a newer format).</returns>
    public async Task<ChapterState?> ReadAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var path = PathFor(itemId);
        try
        {
            if (new FileInfo(path).Length > MaxRecordBytes)
            {
                return null;
            }

            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                var state = await JsonSerializer.DeserializeAsync<ChapterState>(stream, Json, cancellationToken).ConfigureAwait(false);
                return state?.SchemaVersion == SchemaVersion ? state : null;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes an item's record, replacing the old one atomically.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="state">The record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the record is in place.</returns>
    public async Task WriteAsync(Guid itemId, ChapterState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(itemId);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var stream = File.Create(temp);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, state, Json, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp); // no-op once moved
        }
    }

    /// <summary>
    /// Lists the items that have a record.
    /// </summary>
    /// <returns>Item IDs.</returns>
    public IReadOnlyList<Guid> KnownItemIds()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<Guid>();
        }

        return Directory.EnumerateFiles(_directory, "*.json")
            .Select(f => Guid.TryParseExact(Path.GetFileNameWithoutExtension(f), "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }

    private string PathFor(Guid itemId) => Path.Combine(_directory, itemId.ToString("N") + ".json");
}
