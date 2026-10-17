namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>A platform view matched outside the MAUI tree (tab bar button, alert action, toolbar item).</summary>
internal sealed record NativeHit(object View, string Type, string? AutomationId, string? Text, Rect Bounds, bool Enabled, MatchKind Kind);

/// <summary>A platform scroll container and its on-screen viewport in window coordinates.</summary>
internal sealed record NativeScrollable(object View, Rect Viewport, string Type);

internal readonly record struct ScrollOutcome(bool Moved, double OffsetX, double OffsetY);

/// <summary>
/// Platform-level lookups the MAUI tree cannot answer: native-only views, what a finger at a point
/// really hits, and scroll containers (UIScrollView / RecyclerView / ScrollView). Main thread.
/// </summary>
internal static partial class NativeUi
{
    /// <summary>Visible platform views matching by accessibility identifier or text, topmost window first.</summary>
    public static partial List<NativeHit> Find(ElementQuery query);

    /// <summary>
    /// True when a finger at the point lands on the element — its view, a view inside it or a
    /// container that handles its taps; false when something else covers it; null when unknown.
    /// </summary>
    public static partial bool? Reaches(VisualElement element, Point windowPoint);

    /// <summary>
    /// The scroll container to drive: the element itself, one inside it, or (element null) the
    /// largest visible one on screen.
    /// </summary>
    public static partial NativeScrollable? FindScrollable(VisualElement? container);

    /// <summary>The nearest scroll container around the element.</summary>
    public static partial NativeScrollable? ScrollableAround(VisualElement element);

    /// <summary>Moves the content offset by (dx, dy), clamped to the content — no animation.</summary>
    public static partial ScrollOutcome ScrollBy(NativeScrollable scrollable, double dx, double dy);

    /// <summary>Semantic activation of a native view (UIControl actions) when touches cannot be injected.</summary>
    public static partial bool Activate(object nativeView);

    /// <summary>Physical pixels per window unit.</summary>
    public static partial double Scale();
}
