using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Processes a bounded, deduplicated queue of ratings requested by visible library cards.
/// </summary>
public sealed class CsfdCardFetchQueue : BackgroundService
{
    private readonly BoundedDeduplicatingQueue<Guid> _queue;
    private readonly ILibraryManager _libraryManager;
    private readonly CsfdLookupService _lookupService;
    private readonly ILogger<CsfdCardFetchQueue> _logger;
    private readonly int _capacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdCardFetchQueue"/> class.
    /// </summary>
    public CsfdCardFetchQueue(
        ILibraryManager libraryManager,
        CsfdLookupService lookupService,
        ILogger<CsfdCardFetchQueue> logger)
    {
        _libraryManager = libraryManager;
        _lookupService = lookupService;
        _logger = logger;
        _capacity = Math.Clamp(Plugin.Instance?.Configuration.CardFetchQueueLimit ?? 50, 1, 500);
        _queue = new BoundedDeduplicatingQueue<Guid>(_capacity);
    }

    /// <summary>Gets the number of unique pending items.</summary>
    public int Count => _queue.Count;

    /// <summary>Gets the configured queue capacity.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Tries to enqueue an item without waiting or creating duplicates.
    /// </summary>
    public bool TryEnqueue(Guid itemId)
    {
        return _queue.TryEnqueue(itemId);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var itemId in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var configuration = Plugin.Instance?.Configuration;
                if (configuration is not { EnableLibraryCardBadges: true, FetchCardRatingsWhileBrowsing: true })
                {
                    continue;
                }

                var item = _libraryManager.GetItemById(itemId);
                if (item is null
                    || (item.GetType().Name != "Movie" && item.GetType().Name != "Series"))
                {
                    continue;
                }

                await _lookupService.GetBadgeAsync(item, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not load a queued ČSFD card rating for {ItemId}", itemId);
            }
            finally
            {
                _queue.MarkComplete(itemId);
            }
        }
    }
}
