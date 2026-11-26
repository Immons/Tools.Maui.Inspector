namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>
/// Visual roots outside a window's page tree: overlays an app hosts straight on the platform window
/// (a subview of the UIWindow, a child of the Android decor view) or reports itself. Their elements
/// are "attached" only while the root is live. Everything is held weakly.
/// </summary>
internal interface IVisualRoots
{
    /// <summary>True when at least one root was reported — lets the attachment walk skip the lookups otherwise.</summary>
    bool HasExplicit { get; }

    /// <summary>A root the app reported; live until <see cref="RemoveExplicit"/>. False when it was already known.</summary>
    bool AddExplicit(Element root);

    bool RemoveExplicit(Element root);

    bool IsExplicit(Element element);

    IReadOnlyList<Element> Explicit();

    /// <summary>Found hosted on a platform window at least once — its liveness is then the platform view's.</summary>
    bool IsDiscovered(Element element);

    /// <summary>Records the roots found on the platform windows now; returns those new since the last scan and those gone.</summary>
    (List<Element> Appeared, List<Element> Vanished) Scanned(IReadOnlyList<Element> found);

    /// <summary>The roots found by the last scan, bottom to top as the platform window stacks them.</summary>
    IReadOnlyList<Element> Shown();

    /// <summary>Bumped whenever the set of live roots changes — the web panel's cue to reload the tree.</summary>
    int Version { get; }

    /// <summary>The set of live roots changed (raised outside any lock, on the thread that changed it).</summary>
    event Action? Changed;
}
