namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>Registry of weakly-held objects — the population a memory snapshot examines.</summary>
internal interface ITrackedInstances
{
    int Count { get; }

    /// <summary>Starts tracking the object, or returns its existing record.</summary>
    TrackedInstance Track(object target, TrackedKind kind, string? owner);

    bool TryGet(object target, out TrackedInstance record);

    /// <summary>The element left its window (page popped, view removed).</summary>
    void MarkDetached(object target, DateTime when);

    /// <summary>The element is part of a window again — a cached page pushed a second time.</summary>
    void MarkAttached(object target);

    /// <summary>The survivors, without touching the registry — for readers that only look.</summary>
    IReadOnlyList<TrackedInstance> Live();

    /// <summary>Drops the collected records; returns the survivors and what was collected, per type.</summary>
    (List<TrackedInstance> Live, Dictionary<string, int> Collected) Prune();

    /// <summary>Objects collected since the counters were last reset, per type — a baseline resets them.</summary>
    IReadOnlyDictionary<string, int> CollectedSinceReset { get; }

    void ResetCollectedCounters();

    /// <summary>Forgets everything — used when tracking is switched off.</summary>
    void Clear();
}
