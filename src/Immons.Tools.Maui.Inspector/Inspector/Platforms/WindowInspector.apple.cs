using CoreGraphics;
using Microsoft.Maui.Platform;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Inspector;

internal sealed partial class WindowInspector
{
    UIWindow? _uiWindow;
    UILongPressGestureRecognizer? _recognizer;
    UIView? _highlightPlatform;
    UIView? _panelPlatform;

    private partial void AttachPlatform(bool activationGesture)
    {
        _uiWindow = _window.Handler?.PlatformView as UIWindow;
        if (_uiWindow == null || !activationGesture)
            return;

        _recognizer = new UILongPressGestureRecognizer(OnLongPress)
        {
            MinimumPressDuration = _options.LongPressDuration.TotalSeconds,
            NumberOfTouchesRequired = (nuint)Math.Clamp(_options.LongPressTouchCount, 1, 2),
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            AllowableMovement = 16,
            ShouldRecognizeSimultaneously = (_, _) => true,
        };
        _uiWindow.AddGestureRecognizer(_recognizer);
    }

    private partial void DetachPlatform()
    {
        if (_uiWindow != null && _recognizer != null)
            _uiWindow.RemoveGestureRecognizer(_recognizer);
        _recognizer = null;
        _uiWindow = null;
    }

    void OnLongPress(UILongPressGestureRecognizer gesture)
    {
        // Note: remote highlight-only mode keeps IsShown true — OnLongPressDetected decides.
        if (gesture.State != UIGestureRecognizerState.Began || _uiWindow == null)
            return;

        var p = gesture.LocationInView(_uiWindow);
        OnLongPressDetected(new Point(p.X, p.Y));
    }

    private partial void AddLayersPlatform()
    {
        if (_uiWindow == null || _mauiContext == null)
            return;

        _highlightPlatform = _highlightLayer!.ToPlatform(_mauiContext);
        _highlightPlatform.Frame = _uiWindow.Bounds;
        _highlightPlatform.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        _uiWindow.AddSubview(_highlightPlatform);

        if (_panelLayer == null)
            return; // remote highlight-only mode

        _panelPlatform = _panelLayer.ToPlatform(_mauiContext);
        var bounds = _uiWindow.Bounds;
        var height = (nfloat)(bounds.Height * _options.PanelHeightFraction);
        _panelPlatform.Frame = new CGRect(0, bounds.Height - height, bounds.Width, height);
        _panelPlatform.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleTopMargin;
        _panelPlatform.UserInteractionEnabled = true;
        _uiWindow.AddSubview(_panelPlatform);
        // Re-apply any offset after (re)show.
        SetPanelOffsetPlatform(_panelLayer.DragOffsetX, _panelLayer.DragOffsetY);
    }

    private partial void RemoveLayersPlatform()
    {
        _highlightPlatform?.RemoveFromSuperview();
        _panelPlatform?.RemoveFromSuperview();
        _highlightPlatform = null;
        _panelPlatform = null;
    }

    private partial void SetPanelOffsetPlatform(double xDp, double yDp)
    {
        if (_panelPlatform == null)
            return;
        // Direct transform on the UIWindow-hosted view — reliable where MAUI Translation is not.
        _panelPlatform.Transform = CGAffineTransform.MakeTranslation((nfloat)xDp, (nfloat)yDp);
    }

    private partial Rect? GetRectInWindowPlatform(VisualElement element)
    {
        if (_uiWindow == null || element.Handler?.PlatformView is not UIView pv || pv.Window == null)
            return null;

        var rect = pv.ConvertRectToView(pv.Bounds, _uiWindow);
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private partial Point GetLayerOriginPlatform()
    {
        if (_uiWindow == null || _highlightPlatform?.Superview == null)
            return Point.Zero;

        var p = _highlightPlatform.ConvertPointToView(CGPoint.Empty, _uiWindow);
        return new Point(p.X, p.Y);
    }

    private partial double GetBottomInsetPlatform() => _uiWindow?.SafeAreaInsets.Bottom ?? 0;

    // Modals share the UIWindow, so Essentials' Screenshot already captures them.
    private partial byte[]? CapturePngPlatform() => null;

    private partial bool InjectTapPlatform(Point windowDp) => false; // semantic fallback applies
}
