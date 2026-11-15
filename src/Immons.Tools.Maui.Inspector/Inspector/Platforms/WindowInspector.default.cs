namespace Immons.Tools.Maui.Inspector.Inspector;

// No-op implementation for TFMs without a platform (plain net10.0).
internal sealed partial class WindowInspector
{
    private partial void AttachPlatform(bool activationGesture) { }
    private partial void DetachPlatform() { }
    private partial void AddLayersPlatform() { }
    private partial void RemoveLayersPlatform() { }
    private partial void SetPanelOffsetPlatform(double xDp, double yDp) { 
}
    private partial Rect? GetRectInWindowPlatform(VisualElement element) => null;
    private partial Point GetLayerOriginPlatform() => Point.Zero;
    private partial double GetBottomInsetPlatform() => 0;

    private partial byte[]? CapturePngPlatform() => null;

    private partial bool InjectTapPlatform(Point windowDp) => false; // semantic fallback applies
}
