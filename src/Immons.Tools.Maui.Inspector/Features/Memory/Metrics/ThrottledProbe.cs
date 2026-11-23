namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

/// <summary>
/// A slow reading kept fresh without ever blocking the UI: at most one read per interval and one in
/// flight. Off the main thread the read runs in place, so the caller gets a fresh value; on the main
/// thread it is handed to <paramref name="queue"/> (the thread pool) and the caller gets the previous
/// value. A failed read clears the value — a stale number is worse than none.
/// </summary>
internal sealed class ThrottledProbe<T>(TimeSpan interval, Func<T?> read, Action<Action> queue) where T : class
{
    readonly long _intervalMs = (long)interval.TotalMilliseconds;
    long _started = long.MinValue / 2; // "long ago" without overflowing now - started
    int _busy;
    volatile T? _latest;

    public T? Latest => _latest;

    /// <summary>Starts a read if one is due and none is running; returns the latest value.</summary>
    public T? Refresh(bool onMainThread, long nowMs)
    {
        if (nowMs - Interlocked.Read(ref _started) >= _intervalMs && Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _started, nowMs);
            if (onMainThread)
            {
                try
                {
                    queue(Read);
                }
                catch
                {
                    Volatile.Write(ref _busy, 0);
                }
            }
            else
            {
                Read();
            }
        }
        return _latest;
    }

    void Read()
    {
        try
        {
            _latest = read();
        }
        catch
        {
            _latest = null;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
