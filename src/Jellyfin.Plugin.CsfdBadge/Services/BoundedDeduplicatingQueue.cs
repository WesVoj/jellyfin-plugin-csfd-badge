using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Bounded asynchronous queue that stores at most one pending occurrence of each value.
/// </summary>
internal sealed class BoundedDeduplicatingQueue<T>
    where T : notnull
{
    private readonly Channel<T> _channel;
    private readonly ConcurrentDictionary<T, byte> _pending = new();

    public BoundedDeduplicatingQueue(int capacity)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>Gets the number of unique pending values.</summary>
    public int Count => _pending.Count;

    /// <summary>Gets queued values as an asynchronous stream.</summary>
    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Tries to enqueue a unique value without waiting.</summary>
    public bool TryEnqueue(T value)
    {
        if (!_pending.TryAdd(value, 0))
        {
            return true;
        }

        if (_channel.Writer.TryWrite(value))
        {
            return true;
        }

        _pending.TryRemove(value, out _);
        return false;
    }

    /// <summary>Marks a consumed value as no longer pending.</summary>
    public void MarkComplete(T value)
    {
        _pending.TryRemove(value, out _);
    }
}
