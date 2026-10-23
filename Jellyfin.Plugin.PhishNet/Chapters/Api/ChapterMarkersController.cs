using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PhishNet.Chapters.Api;

/// <summary>
/// The settings page's chapter status and the Needs review actions (plan §4.5). Admins only: the policy
/// is on the class, so it covers every action, and none of them opts out.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("PhishData/Chapters")]
[Produces(MediaTypeNames.Application.Json)]
public class ChapterMarkersController : ControllerBase
{
    private readonly ChapterMarkerService _service;
    private readonly ChapterQueue _queue;
    private readonly ILibraryManager _library;
    private readonly Func<PluginConfiguration> _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterMarkersController"/> class.
    /// </summary>
    /// <param name="service">The chapter service.</param>
    /// <param name="queue">The queue, so a Retry runs soon.</param>
    /// <param name="library">The library manager.</param>
    /// <param name="config">Reads the current plugin configuration.</param>
    public ChapterMarkersController(ChapterMarkerService service, ChapterQueue queue, ILibraryManager library, Func<PluginConfiguration> config)
    {
        _service = service;
        _queue = queue;
        _library = library;
        _config = config;
    }

    /// <summary>
    /// Gets the counts by status and the shows that wait for the admin.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChapterStatusReport>> GetStatus(CancellationToken cancellationToken)
    {
        var records = await _service.Store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return ChapterStatusReport.Build(records, id => _library.GetItemById(id), _config().EnableChapterMarkers);
    }

    /// <summary>
    /// Tries a show again on the next pass: re-aligns it, or re-reads its sidecar.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content, or why not.</returns>
    [HttpPost("{itemId}/Retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Retry([FromRoute, Required] Guid itemId, CancellationToken cancellationToken)
    {
        var result = await _service.RetryAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (result.Status == ChapterActionStatus.Done)
        {
            _queue.Enqueue(itemId);
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Saves a review show's aligned chapters despite low confidence.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content, or why not.</returns>
    [HttpPost("{itemId}/ApplyAnyway")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ApplyAnyway([FromRoute, Required] Guid itemId, CancellationToken cancellationToken)
    {
        if (_library.GetItemById(itemId) is not Video video)
        {
            return NotFound(new ChapterActionMessage("the show isn't in the library"));
        }

        return ToActionResult(await _service.ApplyAnywayAsync(video, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Leaves a show alone until a Retry or a change to its file or sidecar.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content, or why not.</returns>
    [HttpPost("{itemId}/Ignore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Ignore([FromRoute, Required] Guid itemId, CancellationToken cancellationToken) =>
        ToActionResult(await _service.IgnoreAsync(itemId, cancellationToken).ConfigureAwait(false));

    private ActionResult ToActionResult(ChapterActionResult result) => result.Status switch
    {
        ChapterActionStatus.Done => NoContent(),
        ChapterActionStatus.NotFound => NotFound(new ChapterActionMessage(result.Message ?? "not found")),
        _ => Conflict(new ChapterActionMessage(result.Message ?? result.Status.ToString())),
    };
}

/// <summary>Why an action didn't happen.</summary>
/// <param name="Message">The reason, for the settings page to show.</param>
public sealed record ChapterActionMessage(string Message);
