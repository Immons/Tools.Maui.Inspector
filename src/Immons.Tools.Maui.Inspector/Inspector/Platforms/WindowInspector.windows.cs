using Microsoft.Maui.Platform;
// Every WinUI type is aliased instead of importing the namespaces: KeyboardAccelerator,
// VerticalAlignment and HorizontalAlignment also exist under Microsoft.Maui.*, which
// ImplicitUsings brings in, so a plain "using Microsoft.UI.Xaml;" makes them ambiguous.
using WAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
using WElement = Microsoft.UI.Xaml.FrameworkElement;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WHoldingArgs = Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs;
using WHoldingHandler = Microsoft.UI.Xaml.Input.HoldingEventHandler;
using WHorizontal = Microsoft.UI.Xaml.HorizontalAlignment;
using WPanel = Microsoft.UI.Xaml.Controls.Panel;
using WSizeChangedHandler = Microsoft.UI.Xaml.SizeChangedEventHandler;
using WTranslate = Microsoft.UI.Xaml.Media.TranslateTransform;
using WUIElement = Microsoft.UI.Xaml.UIElement;
using WVertical = Microsoft.UI.Xaml.VerticalAlignment;
using WWindow = Microsoft.UI.Xaml.Window;
using WWindowActivatedArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;

namespace Immons.Tools.Maui.Inspector.Inspector;

// WinUI: activated with Ctrl+Shift+I (keyboard) or a touch press-and-hold.
internal sealed partial class WindowInspector
{
    WWindow? _platformWindow;
    WPanel? _attachedRoot;
    WAccelerator? _accelerator;
    WHoldingHandler? _holdingHandler;
    WGrid? _host;
    WElement? _highlightPlatform;
    WElement? _panelPlatform;
    WSizeChangedHandler? _hostSizeChanged;

    WPanel? RootPanel => _platformWindow?.Content as WPanel;

    private partial void AttachPlatform(bool activationGesture)
    {
        _platformWindow = _window.Handler?.PlatformView as WWindow;
        if (_platformWindow == null || !activationGesture)
            return;

        if (RootPanel != null)
        {
            AttachToRoot(RootPanel);
        }
        else
        {
            // Content is not assigned yet during handler mapping — hook up on first activation.
            _platformWindow.Activated += OnWindowActivated;
        }
    }

    void OnWindowActivated(object? sender, WWindowActivatedArgs e)
    {
        if (_platformWindow != null && RootPanel is { } root)
        {
            _platformWindow.Activated -= OnWindowActivated;
            AttachToRoot(root);
        }
    }

    void AttachToRoot(WPanel root)
    {
        if (_attachedRoot != null)
            return;
        _attachedRoot = root;

        _accelerator = new WAccelerator
        {
            Key = Windows.System.VirtualKey.I,
            Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift,
        };
        _accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            if (IsShown)
                Hide();
            else
                _window.Dispatcher.Dispatch(() => Show(null));
        };
        root.KeyboardAccelerators.Add(_accelerator);

        _holdingHandler = new WHoldingHandler(OnHolding);
        root.AddHandler(WUIElement.HoldingEvent, _holdingHandler, handledEventsToo: true);
    }

    void OnHolding(object sender, WHoldingArgs e)
    {
        // Note: remote highlight-only mode keeps IsShown true — OnLongPressDetected decides.
        if (e.HoldingState != Microsoft.UI.Input.HoldingState.Started || _attachedRoot == null)
            return;

        var p = e.GetPosition(_attachedRoot);
        OnLongPressDetected(new Point(p.X, p.Y));
    }

    private partial void DetachPlatform()
    {
        if (_platformWindow != null)
            _platformWindow.Activated -= OnWindowActivated;

        if (_attachedRoot != null)
        {
            if (_accelerator != null)
                _attachedRoot.KeyboardAccelerators.Remove(_accelerator);
            if (_holdingHandler != null)
                _attachedRoot.RemoveHandler(WUIElement.HoldingEvent, _holdingHandler);
        }

        _accelerator = null;
        _holdingHandler = null;
        _attachedRoot = null;
        _platformWindow = null;
    }

    private partial void AddLayersPlatform()
    {
        if (RootPanel is not { } root || _mauiContext == null)
            return;

        _host = new WGrid();

        _highlightPlatform = _highlightLayer!.ToPlatform(_mauiContext) as WElement;
        if (_highlightPlatform != null)
            _host.Children.Add(_highlightPlatform);

        _panelPlatform = _panelLayer?.ToPlatform(_mauiContext) as WElement;
        if (_panelPlatform != null)
        {
            _panelPlatform.VerticalAlignment = WVertical.Bottom;
            _panelPlatform.HorizontalAlignment = WHorizontal.Stretch;
            _panelPlatform.Height = Math.Max(200, root.ActualHeight * _options.PanelHeightFraction);
            _host.Children.Add(_panelPlatform);

            _hostSizeChanged = (_, e) =>
            {
                if (_panelPlatform != null)
                    _panelPlatform.Height = Math.Max(200, e.NewSize.Height * _options.PanelHeightFraction);
            };
            _host.SizeChanged += _hostSizeChanged;
        }

        root.Children.Add(_host);
        if (_panelLayer != null)
            SetPanelOffsetPlatform(_panelLayer.DragOffsetX, _panelLayer.DragOffsetY);
    }

    private partial void RemoveLayersPlatform()
    {
        if (_host != null)
        {
            if (_hostSizeChanged != null)
                _host.SizeChanged -= _hostSizeChanged;
            (_host.Parent as WPanel)?.Children.Remove(_host);
        }
        _hostSizeChanged = null;
        _host = null;
        _highlightPlatform = null;
        _panelPlatform = null;
    }

    private partial void SetPanelOffsetPlatform(double xDp, double yDp)
    {
        if (_panelPlatform == null)
            return;
        _panelPlatform.RenderTransform = new WTranslate
        {
            X = xDp,
            Y = yDp,
        };
    }

    private partial Rect? GetRectInWindowPlatform(VisualElement element)
    {
        if (RootPanel is not { } root
            || element.Handler?.PlatformView is not WElement fe
            || fe.XamlRoot == null)
            return null;

        try
        {
            var transform = fe.TransformToVisual(root);
            var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            return new Rect(topLeft.X, topLeft.Y, fe.ActualWidth, fe.ActualHeight);
        }
        catch
        {
            return null;
        }
    }

    private partial Point GetLayerOriginPlatform() => Point.Zero;

    private partial double GetBottomInsetPlatform() => 0;

    // Modals render inside the same XAML root, Essentials' Screenshot captures them.
    private partial byte[]? CapturePngPlatform() => null;

    private partial bool InjectTapPlatform(Point windowDp) => false; // semantic fallback applies
}
