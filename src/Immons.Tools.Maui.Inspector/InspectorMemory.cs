namespace Immons.Tools.Maui.Inspector;

/// <summary>
/// Visual roots the Memory view cannot find on its own. Instance tracking follows the window's page
/// tree, and — with <see cref="MemoryOptions.DiscoverVisualRoots"/> — MAUI views hosted straight on
/// the platform window. A root kept anywhere else (a native container of your own, a second
/// platform window) is reported here: its elements are tracked like a page's, counted as attached
/// while it is tracked and judged as detached once it is untracked.
/// </summary>
/// <remarks>
/// An element reported through <see cref="Navigation.ReportPushed"/> is a visual root too, until
/// <see cref="Navigation.ReportPopped"/>. Only weak references are kept: forgetting to untrack a
/// root never keeps it alive, it only keeps its elements counted as attached while they live.
/// Reached as <c>Immons.Tools.Maui.Inspector.Memory</c>, like <see cref="Navigation"/>.
/// </remarks>
/// <example>
/// <code>
/// overlayRoot.Parent = window;
/// uiWindow.AddSubview(overlayRoot.ToPlatform(mauiContext));
/// Immons.Tools.Maui.Inspector.Memory.TrackVisualRoot(overlayRoot);
/// …
/// overlayRoot.Handler?.PlatformView … RemoveFromSuperview();
/// Immons.Tools.Maui.Inspector.Memory.UntrackVisualRoot(overlayRoot);
/// </code>
/// </example>
public static class Memory
{
    /// <summary>Starts tracking the root's tree; it counts as attached until <see cref="UntrackVisualRoot"/>.</summary>
    public static void TrackVisualRoot(Element root) =>
        Inspector.InspectorServices.Current.Tracker.TrackRoot(root);

    /// <summary>The root went away: what is under it and nowhere else becomes a detached candidate for the next snapshot.</summary>
    public static void UntrackVisualRoot(Element root) =>
        Inspector.InspectorServices.Current.Tracker.UntrackRoot(root);
}
