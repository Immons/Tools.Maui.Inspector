namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>One weakly-held object the tracker knows about, with the facts a snapshot classifies it by.</summary>
internal sealed class TrackedInstance(int id, TrackedKind kind, Type type, object target, DateTime firstSeen)
{
    readonly WeakReference _target = new(target);

    public int Id { get; } = id;

    public TrackedKind Kind { get; } = kind;

    public Type Type { get; } = type;

    public DateTime FirstSeen { get; } = firstSeen;

    /// <summary>The element type that owned a view model / handler / platform view when it was first seen.</summary>
    public string? Owner { get; set; }

    /// <summary>When the element left its window, or when another object was first found orphaned.</summary>
    public DateTime? DetachedAt { get; set; }

    /// <summary>Snapshots — each a full round of collections — the object survived while detached.</summary>
    public int DetachedSnapshots { get; set; }

    public object? Target => _target.Target;

    public bool IsAlive => _target.IsAlive;
}
