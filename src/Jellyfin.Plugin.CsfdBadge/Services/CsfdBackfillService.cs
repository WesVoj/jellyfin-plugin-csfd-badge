using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Runs one administrator-controlled, sequential ČSFD library backfill.
/// </summary>
public sealed class CsfdBackfillService : IHostedService, IDisposable
{
    private readonly object _sync = new();
    private readonly ILibraryManager _libraryManager;
    private readonly CsfdLookupService _lookupService;
    private readonly CsfdCardFetchQueue _cardFetchQueue;
    private readonly CsfdBackfillProgressTracker _progress = new();
    private readonly ILogger<CsfdBackfillService> _logger;
    private readonly CancellationTokenSource _serviceCancellation = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private volatile bool _paused;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdBackfillService"/> class.
    /// </summary>
    public CsfdBackfillService(
        ILibraryManager libraryManager,
        CsfdLookupService lookupService,
        CsfdCardFetchQueue cardFetchQueue,
        ILogger<CsfdBackfillService> logger)
    {
        _libraryManager = libraryManager;
        _lookupService = lookupService;
        _cardFetchQueue = cardFetchQueue;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _serviceCancellation.Cancel();
        Task? runTask;
        lock (_sync)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask is not null)
        {
            try
            {
                await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during Jellyfin shutdown.
            }
        }
    }

    /// <summary>Gets a snapshot of both queue and backfill progress.</summary>
    public Models.CsfdAdminStatusResponse GetStatus()
        => _progress.Snapshot(_cardFetchQueue.Count, _cardFetchQueue.Capacity);

    /// <summary>Starts a new backfill of missing or stale movie and series ratings.</summary>
    public Models.CsfdAdminStatusResponse StartBackfill()
    {
        lock (_sync)
        {
            if (_progress.IsActive)
            {
                return GetStatus();
            }

            var libraryItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
                Recursive = true
            });
            var pending = new List<Guid>(libraryItems.Count);
            foreach (var item in libraryItems)
            {
                _lookupService.GetCachedBadge(item, out var needsRefresh);
                if (needsRefresh)
                {
                    pending.Add(item.Id);
                }
            }

            _paused = false;
            _progress.Begin(libraryItems.Count, pending.Count, libraryItems.Count - pending.Count);
            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_serviceCancellation.Token);
            _runTask = Task.Run(() => RunAsync(pending, _runCancellation.Token), CancellationToken.None);
            _logger.LogInformation(
                "Started ČSFD backfill with {Pending} pending and {Skipped} cached items",
                pending.Count,
                libraryItems.Count - pending.Count);
            return GetStatus();
        }
    }

    /// <summary>Pauses the active backfill after its current request.</summary>
    public Models.CsfdAdminStatusResponse Pause()
    {
        if (_progress.TryPause())
        {
            _paused = true;
        }

        return GetStatus();
    }

    /// <summary>Resumes a paused backfill.</summary>
    public Models.CsfdAdminStatusResponse Resume()
    {
        if (_progress.TryResume())
        {
            _paused = false;
        }

        return GetStatus();
    }

    /// <summary>Stops the active backfill.</summary>
    public Models.CsfdAdminStatusResponse Stop()
    {
        lock (_sync)
        {
            if (_progress.TryStopping())
            {
                _paused = false;
                _runCancellation?.Cancel();
            }

            return GetStatus();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _runCancellation?.Dispose();
        _serviceCancellation.Dispose();
    }

    private async Task RunAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var itemId in itemIds)
            {
                while (_paused)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var item = _libraryManager.GetItemById(itemId);
                if (item is null)
                {
                    _progress.MarkFailed("Library item disappeared during backfill.");
                    continue;
                }

                _progress.SetCurrent(item.Name);
                try
                {
                    var badge = await _lookupService.GetBadgeAsync(item, cancellationToken).ConfigureAwait(false);
                    if (badge is null)
                    {
                        _progress.MarkNotFound();
                    }
                    else
                    {
                        _progress.MarkSucceeded();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _progress.MarkFailed(exception.Message);
                    _logger.LogWarning(exception, "ČSFD backfill failed for {ItemId}", itemId);
                }
            }

            _progress.Finish("Completed");
            _logger.LogInformation("ČSFD backfill completed");
        }
        catch (OperationCanceledException)
        {
            _progress.Finish("Stopped");
            _logger.LogInformation("ČSFD backfill stopped");
        }
        catch (Exception exception)
        {
            _progress.Finish("Failed", exception.Message);
            _logger.LogError(exception, "ČSFD backfill terminated unexpectedly");
        }
    }
}
