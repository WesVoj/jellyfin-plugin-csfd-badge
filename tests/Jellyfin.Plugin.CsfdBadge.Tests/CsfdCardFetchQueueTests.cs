using Jellyfin.Plugin.CsfdBadge.Services;
using Xunit;

namespace Jellyfin.Plugin.CsfdBadge.Tests;

public sealed class CsfdCardFetchQueueTests
{
    [Fact]
    public void TryEnqueue_DeduplicatesAndEnforcesCapacity()
    {
        var queue = new BoundedDeduplicatingQueue<Guid>(capacity: 2);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));
        Assert.False(queue.TryEnqueue(Guid.NewGuid()));
        Assert.Equal(2, queue.Count);
    }
}
