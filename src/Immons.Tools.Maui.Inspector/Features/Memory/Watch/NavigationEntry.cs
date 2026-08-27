namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

internal enum PageVerdict
{
    /// <summary>Still on screen.</summary>
    Open,

    /// <summary>Left the screen; no snapshot has judged it yet.</summary>
    Pending,

    Collected,

    /// <summary>Survived the collections after leaving — the leak.</summary>
    Alive,

    /// <summary>Came back on screen after leaving — a cached page, not a leak.</summary>
    Reattached,
}

/// <summary>
/// One screen's life in the navigation ledger, with the memory readings around it. A screen is
/// whatever the app puts in front of the user and takes away again — a Page it pushed, or a view
/// an app's own overlay host reported through <see cref="Immons.Tools.Maui.Inspector.Navigation.ReportPushed"/>.
/// </summary>
internal sealed class NavigationEntry(int id, object screen, string type, string label, DateTime pushedAt, long managedAtPush, long? processAtPush)
{
    readonly WeakReference<object> _screen = new(screen);

    public int Id { get; } = id;

    public string Type { get; } = type;

    public string Label { get; } = label;

    public DateTime PushedAt { get; } = pushedAt;

    public long ManagedAtPush { get; } = managedAtPush;

    public long? ProcessAtPush { get; } = processAtPush;

    public DateTime? PoppedAt { get; set; }

    public long? ManagedAtPop { get; set; }

    public long? ProcessAtPop { get; set; }

    /// <summary>Reported by the app rather than seen in the visual tree.</summary>
    public bool Reported { get; init; }

    public PageVerdict Verdict { get; set; } = PageVerdict.Open;

    /// <summary>Snapshots the screen survived after leaving.</summary>
    public int Survived { get; set; }

    public bool Is(object candidate) => _screen.TryGetTarget(out var screen) && ReferenceEquals(screen, candidate);

    public bool IsAlive => _screen.TryGetTarget(out _);
}
