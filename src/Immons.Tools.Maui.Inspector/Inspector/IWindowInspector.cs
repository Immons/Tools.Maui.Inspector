namespace Immons.Tools.Maui.Inspector.Inspector;

/// <summary>
/// The inspector surface consumed by the web server endpoints — everything remote callers
/// may do with a window without touching the overlay internals.
/// </summary>
internal interface IWindowInspector
{
    IDispatcher Dispatcher { get; }

    IMauiContext? MauiContext { get; }

    VisualElement? SelectedElement { get; }

    VisualElement? CompareElement { get; }

    bool MeasureMode { get; }

    bool RemoteSelectModeActive { get; }

    bool OverlayShown { get; }

    bool DebugPaintActive { get; }

    /// <summary>
    /// Bottom to top: the window's page, then its modals and the visual roots hosted outside the
    /// page tree (overlays on the platform window, reported roots) in platform stacking order.
    /// </summary>
    IEnumerable<VisualElement> Roots { get; }

    /// <summary>The tree's marker for a root that is not the page or a modal ("window overlay"), else null.</summary>
    string? RootNote(VisualElement root);

    Size WindowSize { get; }

    Rect? BoundsOf(VisualElement element);

    /// <summary>
    /// Platform-composited screenshot covering modal windows, or null when the plain
    /// <see cref="Screenshot"/> capture is already correct. Main thread only.
    /// </summary>
    byte[]? CapturePng();

    string BuildDump();

    bool RemoteSelectAt(Point windowPoint);

    /// <summary>Delivers a real (or semantic) tap to the app at the given window-dp point.</summary>
    bool RemoteTapAt(Point windowPoint);

    /// <summary>Types text / presses an editing key in the app's focused text input.</summary>
    bool RemoteKey(string? text, string? key);

    void RemoteSelect(VisualElement element);

    void RemoteMeasure(VisualElement primary, VisualElement? compare);

    void SetRemoteMeasureMode(bool on);

    void SetRemoteSelectMode(bool on);

    void SetOverlayShown(bool on);

    void SetDebugPaint(bool on);

    void RemoteClearHighlight();

    void RemoteAfterEdit();
}
