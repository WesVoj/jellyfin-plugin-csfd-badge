using Jellyfin.Plugin.CsfdBadge.Services;
using Xunit;

namespace Jellyfin.Plugin.CsfdBadge.Tests;

public sealed class CsfdBackfillProgressTrackerTests
{
    [Fact]
    public void Snapshot_ReportsProgressAndOutcomeCounts()
    {
        var tracker = new CsfdBackfillProgressTracker();
        tracker.Begin(libraryItems: 100, total: 60, skipped: 40);
        tracker.SetCurrent("Test title");
        tracker.MarkSucceeded();
        tracker.MarkNotFound();
        tracker.MarkFailed("Network error");

        var status = tracker.Snapshot(lazyQueueSize: 4, lazyQueueLimit: 50);

        Assert.Equal("Running", status.State);
        Assert.Equal(100, status.LibraryItems);
        Assert.Equal(60, status.Total);
        Assert.Equal(3, status.Processed);
        Assert.Equal(57, status.Remaining);
        Assert.Equal(1, status.Succeeded);
        Assert.Equal(1, status.NotFound);
        Assert.Equal(1, status.Failed);
        Assert.Equal(40, status.Skipped);
        Assert.Equal(5, status.ProgressPercent);
        Assert.Equal("Test title", status.CurrentTitle);
        Assert.Equal("Network error", status.LastError);
        Assert.Equal(4, status.LazyQueueSize);
        Assert.Equal(50, status.LazyQueueLimit);
    }

    [Fact]
    public void PauseResumeAndFinish_UpdateState()
    {
        var tracker = new CsfdBackfillProgressTracker();
        tracker.Begin(libraryItems: 0, total: 0, skipped: 0);

        Assert.True(tracker.TryPause());
        Assert.Equal("Paused", tracker.Snapshot(0, 50).State);

        Assert.True(tracker.TryResume());
        Assert.Equal("Running", tracker.Snapshot(0, 50).State);

        tracker.Finish("Completed");
        var completed = tracker.Snapshot(0, 50);
        Assert.Equal("Completed", completed.State);
        Assert.Equal(100, completed.ProgressPercent);
        Assert.NotNull(completed.FinishedAtUtc);
    }
}
