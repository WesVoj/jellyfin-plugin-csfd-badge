using Jellyfin.Plugin.CsfdBadge.Models;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Thread-safe mutable state for one administrator-triggered backfill run.
/// </summary>
internal sealed class CsfdBackfillProgressTracker
{
    private readonly object _sync = new();
    private string _state = "Idle";
    private int _libraryItems;
    private int _total;
    private int _processed;
    private int _succeeded;
    private int _notFound;
    private int _failed;
    private int _skipped;
    private string? _currentTitle;
    private string? _lastError;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _finishedAtUtc;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _state is "Running" or "Paused" or "Stopping";
            }
        }
    }

    public void Begin(int libraryItems, int total, int skipped)
    {
        lock (_sync)
        {
            _state = "Running";
            _libraryItems = libraryItems;
            _total = total;
            _processed = 0;
            _succeeded = 0;
            _notFound = 0;
            _failed = 0;
            _skipped = skipped;
            _currentTitle = null;
            _lastError = null;
            _startedAtUtc = DateTimeOffset.UtcNow;
            _finishedAtUtc = null;
        }
    }

    public void SetCurrent(string? title)
    {
        lock (_sync)
        {
            _currentTitle = title;
        }
    }

    public void MarkSucceeded() => MarkCompleted(static tracker => tracker._succeeded++);

    public void MarkNotFound() => MarkCompleted(static tracker => tracker._notFound++);

    public void MarkFailed(string error)
    {
        lock (_sync)
        {
            _failed++;
            _processed++;
            _lastError = error;
        }
    }

    public bool TryPause() => TrySetState("Running", "Paused");

    public bool TryResume() => TrySetState("Paused", "Running");

    public bool TryStopping()
    {
        lock (_sync)
        {
            if (_state is not ("Running" or "Paused"))
            {
                return false;
            }

            _state = "Stopping";
            return true;
        }
    }

    public void Finish(string state, string? error = null)
    {
        lock (_sync)
        {
            _state = state;
            _currentTitle = null;
            if (!string.IsNullOrWhiteSpace(error))
            {
                _lastError = error;
            }

            _finishedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public CsfdAdminStatusResponse Snapshot(int lazyQueueSize, int lazyQueueLimit)
    {
        lock (_sync)
        {
            return new CsfdAdminStatusResponse
            {
                State = _state,
                LibraryItems = _libraryItems,
                Total = _total,
                Processed = _processed,
                Remaining = Math.Max(0, _total - _processed),
                Succeeded = _succeeded,
                NotFound = _notFound,
                Failed = _failed,
                Skipped = _skipped,
                ProgressPercent = _total == 0 ? (_state == "Idle" ? 0 : 100) : (int)Math.Floor(_processed * 100d / _total),
                CurrentTitle = _currentTitle,
                LastError = _lastError,
                StartedAtUtc = _startedAtUtc,
                FinishedAtUtc = _finishedAtUtc,
                LazyQueueSize = lazyQueueSize,
                LazyQueueLimit = lazyQueueLimit
            };
        }
    }

    private void MarkCompleted(Action<CsfdBackfillProgressTracker> update)
    {
        lock (_sync)
        {
            update(this);
            _processed++;
        }
    }

    private bool TrySetState(string expected, string next)
    {
        lock (_sync)
        {
            if (_state != expected)
            {
                return false;
            }

            _state = next;
            return true;
        }
    }
}
