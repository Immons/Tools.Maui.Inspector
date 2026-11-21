namespace Immons.Tools.Maui.Inspector.Features.SignalR;

/// <summary>
/// Fixed-capacity circular buffer of one hub's events. Adding past the capacity overwrites the
/// oldest entry in O(1), so a chatty hub costs a bounded amount of memory however long it runs.
/// Thread-safe: the receive loop, the send path and the HTTP server touch it concurrently.
/// </summary>
internal sealed class SignalREventBuffer
{
    readonly object _gate = new();
    readonly SignalREvent?[] _items;
    int _head;   // index of the oldest entry
    int _count;
    long _dropped;

    public SignalREventBuffer(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be at least 1");
        _items = new SignalREvent?[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get
        {
            lock (_gate)
                return _count;
        }
    }

    /// <summary>How many events were overwritten because the buffer was full.</summary>
    public long Dropped
    {
        get
        {
            lock (_gate)
                return _dropped;
        }
    }

    public void Add(SignalREvent item)
    {
        lock (_gate)
        {
            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = item;
                _count++;
            }
            else
            {
                _items[_head] = item;
                _head = (_head + 1) % _items.Length;
                _dropped++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_items);
            _head = 0;
            _count = 0;
            _dropped = 0;
        }
    }

    /// <summary>Events with <see cref="SignalREvent.Seq"/> greater than <paramref name="since"/>, oldest first.</summary>
    public List<SignalREvent> Since(long since)
    {
        lock (_gate)
        {
            var result = new List<SignalREvent>();
            for (var i = 0; i < _count; i++)
            {
                var item = _items[(_head + i) % _items.Length]!;
                if (item.Seq > since)
                    result.Add(item);
            }
            return result;
        }
    }

    public SignalREvent? Find(long seq)
    {
        lock (_gate)
        {
            for (var i = 0; i < _count; i++)
            {
                var item = _items[(_head + i) % _items.Length]!;
                if (item.Seq == seq)
                    return item;
            }
            return null;
        }
    }
}
