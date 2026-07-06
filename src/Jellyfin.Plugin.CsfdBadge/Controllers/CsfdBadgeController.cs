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

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdBadgeController"/> class.
    /// </summary>
    public CsfdBadgeController(ILibraryManager libraryManager, CsfdLookupService lookupService)
    {
        _libraryManager = libraryManager;
        _lookupService = lookupService;
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
}
