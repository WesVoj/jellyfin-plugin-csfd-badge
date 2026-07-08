using Jellyfin.Plugin.CsfdBadge.Models;
using Jellyfin.Plugin.CsfdBadge.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CsfdBadge.Controllers;

/// <summary>
/// API consumed by the web badge.
/// </summary>
[ApiController]
[Authorize]
[Route("CsfdBadge")]
public sealed class CsfdBadgeController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly CsfdLookupService _lookupService;
    private readonly CsfdCardFetchQueue _cardFetchQueue;
    private readonly CsfdBackfillService _backfillService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdBadgeController"/> class.
    /// </summary>
    public CsfdBadgeController(
        ILibraryManager libraryManager,
        CsfdLookupService lookupService,
        CsfdCardFetchQueue cardFetchQueue,
        CsfdBackfillService backfillService)
    {
        _libraryManager = libraryManager;
        _lookupService = lookupService;
        _cardFetchQueue = cardFetchQueue;
        _backfillService = backfillService;
    }

    /// <summary>Gets lazy queue and manual backfill progress.</summary>
    [HttpGet("Admin/Status")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CsfdAdminStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<CsfdAdminStatusResponse> GetAdminStatus() => Ok(_backfillService.GetStatus());

    /// <summary>Starts a manual backfill of missing and stale ratings.</summary>
    [HttpPost("Admin/Backfill/Start")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CsfdAdminStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<CsfdAdminStatusResponse> StartBackfill() => Ok(_backfillService.StartBackfill());

    /// <summary>Pauses the active manual backfill.</summary>
    [HttpPost("Admin/Backfill/Pause")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CsfdAdminStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<CsfdAdminStatusResponse> PauseBackfill() => Ok(_backfillService.Pause());

    /// <summary>Resumes a paused manual backfill.</summary>
    [HttpPost("Admin/Backfill/Resume")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CsfdAdminStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<CsfdAdminStatusResponse> ResumeBackfill() => Ok(_backfillService.Resume());

    /// <summary>Stops the active manual backfill.</summary>
    [HttpPost("Admin/Backfill/Stop")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CsfdAdminStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<CsfdAdminStatusResponse> StopBackfill() => Ok(_backfillService.Stop());

    /// <summary>
    /// Gets settings used by the injected web component.
    /// </summary>
    [HttpGet("ClientConfiguration")]
    [ProducesResponseType(typeof(CsfdBadgeClientConfiguration), StatusCodes.Status200OK)]
    public ActionResult<CsfdBadgeClientConfiguration> GetClientConfiguration()
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return Ok(new CsfdBadgeClientConfiguration
        {
            EnableLibraryCardBadges = configuration.EnableLibraryCardBadges,
            FetchCardRatingsWhileBrowsing = configuration.FetchCardRatingsWhileBrowsing
        });
    }

    /// <summary>
    /// Gets cached badges for visible library cards and optionally queues missing ratings.
    /// </summary>
    [HttpPost("Items/Batch")]
    [ProducesResponseType(typeof(CsfdBadgeBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<CsfdBadgeBatchResponse> GetBatch([FromBody] CsfdBadgeBatchRequest request)
    {
        if (request.ItemIds is null)
        {
            return BadRequest("ItemIds is required.");
        }

        if (request.ItemIds.Count > 100)
        {
            return BadRequest("A batch may contain at most 100 item IDs.");
        }

        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var response = new CsfdBadgeBatchResponse();
        foreach (var requestedId in request.ItemIds
                     .Where(static id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = GetSupportedItem(requestedId);
            if (item is null)
            {
                continue;
            }

            var badge = _lookupService.GetCachedBadge(item, out var needsRefresh);
            if (badge is not null)
            {
                response.Items[requestedId] = badge;
            }

            if (needsRefresh
                && configuration.EnableLibraryCardBadges
                && configuration.FetchCardRatingsWhileBrowsing)
            {
                _cardFetchQueue.TryEnqueue(item.Id);
                response.PendingItemIds.Add(requestedId);
            }
        }

        response.QueueSize = _cardFetchQueue.Count;
        return Ok(response);
    }

    /// <summary>
    /// Gets a ČSFD badge for a movie or series.
    /// </summary>
    [HttpGet("Items/{itemId}")]
    [ProducesResponseType(typeof(CsfdBadgeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsfdBadgeResponse>> GetBadge(
        string itemId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(itemId, out var id))
        {
            return NotFound();
        }

        var item = _libraryManager.GetItemById(id);
        if (item is null
            || (item.GetType().Name != "Movie" && item.GetType().Name != "Series"))
        {
            return NoContent();
        }

        try
        {
            var badge = await _lookupService.GetBadgeAsync(item, cancellationToken).ConfigureAwait(false);
            return badge is null ? NoContent() : Ok(badge);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Explicitly pairs a movie or series with a ČSFD title.
    /// </summary>
    [HttpPost("Items/{itemId}/ManualMatch")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(CsfdBadgeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsfdBadgeResponse>> SetManualMatch(
        string itemId,
        [FromBody] CsfdManualMatchRequest request,
        CancellationToken cancellationToken)
    {
        var item = GetSupportedItem(itemId);
        if (item is null)
        {
            return NotFound();
        }

        try
        {
            return Ok(await _lookupService.SetManualMatchAsync(item, request.CsfdId, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Clears a manual pairing and allows automatic matching to run again.
    /// </summary>
    [HttpDelete("Items/{itemId}/ManualMatch")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearManualMatch(
        string itemId,
        CancellationToken cancellationToken)
    {
        var item = GetSupportedItem(itemId);
        if (item is null)
        {
            return NotFound();
        }

        await _lookupService.ClearManualMatchAsync(item, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private MediaBrowser.Controller.Entities.BaseItem? GetSupportedItem(string itemId)
    {
        if (!Guid.TryParse(itemId, out var id))
        {
            return null;
        }

        var item = _libraryManager.GetItemById(id);
        return item is not null
               && (item.GetType().Name == "Movie" || item.GetType().Name == "Series")
            ? item
            : null;
    }
}
