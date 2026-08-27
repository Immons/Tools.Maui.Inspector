using System.Runtime.CompilerServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>
/// Weak registry: the table maps an object to its record without keeping it alive (the record only
/// holds a weak reference back), and the list is what a snapshot walks. Dead records are dropped
/// on every snapshot and, so a long session without snapshots stays bounded, every few thousand adds.
/// </summary>
internal sealed class TrackedInstances : ITrackedInstances
{
    const int HousekeepingInterval = 5000;

    readonly object _gate = new();
    readonly ConditionalWeakTable<object, TrackedInstance> _byTarget = new();
    readonly List<TrackedInstance> _all = [];
    readonly Dictionary<string, int> _collectedSinceReset = [];
    int _next;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _all.Count;
            }
        }
    }

    public TrackedInstance Track(object target, TrackedKind kind, string? owner)
    {
        lock (_gate)
        {
            if (_byTarget.TryGetValue(target, out var existing))
                return existing;

            var record = new TrackedInstance(++_next, kind, target.GetType(), target, DateTime.Now) { Owner = owner };
            _byTarget.Add(target, record);
            _all.Add(record);
            if (_next % HousekeepingInterval == 0)
                _all.RemoveAll(r => !r.IsAlive);
            return record;
        }
    }

    public bool TryGet(object target, out TrackedInstance record)
    {
        lock (_gate)
        {
            return _byTarget.TryGetValue(target, out record!);
        }
    }

    public void MarkDetached(object target, DateTime when)
    {
        if (TryGet(target, out var record))
            record.DetachedAt ??= when;
    }

    public void MarkAttached(object target)
    {
        if (!TryGet(target, out var record))
            return;
        record.DetachedAt = null;
        record.DetachedSnapshots = 0;
    }

    public IReadOnlyList<TrackedInstance> Live()
    {
        lock (_gate)
        {
            return _all.Where(r => r.IsAlive).ToList();
        }
    }

    public (List<TrackedInstance> Live, Dictionary<string, int> Collected) Prune()
    {
        lock (_gate)
        {
            var live = new List<TrackedInstance>(_all.Count);
            var collected = new Dictionary<string, int>();
            foreach (var record in _all)
            {
                if (record.IsAlive)
                {
                    live.Add(record);
                    continue;
                }
                var key = TypeNames.Full(record.Type);
                collected[key] = collected.GetValueOrDefault(key) + 1;
                // Cumulative too: a second snapshot right after the first finds nothing newly dead,
                // which says nothing about the heap's health — the running total does.
                _collectedSinceReset[key] = _collectedSinceReset.GetValueOrDefault(key) + 1;
            }
            _all.Clear();
            _all.AddRange(live);
            return (live, collected);
        }
    }

    public IReadOnlyDictionary<string, int> CollectedSinceReset
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_collectedSinceReset);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _all.Clear();
            // The table too, or Track() would keep returning the records that are no longer in the
            // list and turning tracking back on would never refill it.
            _byTarget.Clear();
            _collectedSinceReset.Clear();
        }
    }

    public void ResetCollectedCounters()
    {
        lock (_gate)
        {
            _collectedSinceReset.Clear();
        }
    }
}
