

namespace Immons.Tools.Maui.Inspector.Inspector;

/// <summary>
/// Per-window inspector: owns the activation gesture, the highlight layer and the bottom panel.
/// Platform pieces live in the .android/.apple/.default partials.
/// </summary>
internal sealed partial class WindowInspector
{
    readonly Window _window;
    readonly MauiInspectorOptions _options;

    IMauiContext? _mauiContext;
    HighlightLayer? _highlightLayer;
    PanelLayer? _panelLayer;
    VisualElement? _selected;
    VisualElement? _compare;
    bool _measureMode;

    public bool IsShown { get; private set; }

    public WindowInspector(Window window, MauiInspectorOptions options)
    {
        _window = window;
        _options = options;
        AppForegroundState.Track(window);
        HookModalStack();
        _window.SizeChanged += OnWindowSizeChanged;
    }

    /// <summary>
    /// Rotation rebuilds adaptive layouts and re-lays everything out — a kept selection
    /// would point at a stale element and paint its pre-rotation box over the new layout.
    /// </summary>
    void OnWindowSizeChanged(object? sender, EventArgs e)
    {
        if (_selected == null && _compare == null)
            return;
        _selected = null;
        _compare = null;
        UpdateHighlight();
    }

    /// <summary>
    /// On Android every modal page opens in its own platform window (a Dialog) above the
    /// activity, so the overlay layers must move to the new topmost window whenever the
    /// modal stack changes — and back down when it unwinds. On the other platforms the
    /// rehome is a cheap re-add to the same host.
    /// </summary>
    void HookModalStack()
    {
        if (Application.Current is not { } app)
            return;
        app.ModalPushed += OnModalPushed;
        app.ModalPopped += OnModalPopped;
    }

    void UnhookModalStack()
    {
        if (Application.Current is not { } app)
            return;
        app.ModalPushed -= OnModalPushed;
        app.ModalPopped -= OnModalPopped;
    }

    void OnModalPushed(object? sender, ModalPushedEventArgs e)
    {
        // The platform window hosting the modal may not exist yet — rehome once it does.
        if (e.Modal is { } modal && modal.Handler?.PlatformView == null)
        {
            modal.Loaded += OnceLoaded;
            return;

            void OnceLoaded(object? s, EventArgs args)
            {
                modal.Loaded -= OnceLoaded;
                RehomeLayers();
            }
        }

        RehomeLayers();
    }

    void OnModalPopped(object? sender, ModalPoppedEventArgs e) => RehomeLayers();

    void RehomeLayers()
    {
        if (!IsShown)
            return;

        _window.Dispatcher.Dispatch(() =>
        {
            if (!IsShown)
                return;
            RemoveLayersPlatform();
            AddLayersPlatform();
            RefreshTree();
            UpdateHighlight();
        });
    }

    /// <summary>Called every time the window (re)connects to a platform handler.</summary>
    public void OnHandlerChanged()
    {
        Hide();
        DetachPlatform();
        _mauiContext = _window.Handler?.MauiContext;
        if (_mauiContext != null && _options.Activation == InspectorActivation.LongPress)
            AttachPlatform();
    }

    public void Detach()
    {
        UnhookModalStack();
        _window.SizeChanged -= OnWindowSizeChanged;
        DetachPlatform();
    }

    public void Show(Point? windowPoint)
    {
        _mauiContext ??= _window.Handler?.MauiContext;
        if (_mauiContext == null)
            return;

        // Upgrade from remote highlight-only mode to the full overlay.
        if (IsShown && _panelLayer == null)
            Hide();

        if (!IsShown)
        {
            BuildLayers();
            AddLayersPlatform();
            IsShown = true;
            SetSelectMode(true);
            RefreshTree();
        }

        if (windowPoint is { } p)
            SelectAt(p);
        else if (_selected == null && RootElements().LastOrDefault() is { } root)
            SelectElement(root);
    }

    public void Hide()
    {
        if (!IsShown)
            return;

        RemoveLayersPlatform();
        _highlightLayer?.Handler?.DisconnectHandler();
        _panelLayer?.Handler?.DisconnectHandler();
        _highlightLayer = null;
        _panelLayer = null;
        _selected = null;
        _compare = null;
        _measureMode = false;
        IsShown = false;
    }

    void BuildLayers()
    {
        _highlightLayer = new HighlightLayer();
        _highlightLayer.Tapped += p => SelectAt(new Point(p.X + LayerOrigin.X, p.Y + LayerOrigin.Y));

        _panelLayer = new PanelLayer();
        _panelLayer.CloseRequested += Hide;
        _panelLayer.RefreshRequested += () =>
        {
            RefreshTree();
            if (_selected != null)
            {
                // Keep compare if still valid; just re-measure.
                UpdateHighlight();
                if (!_measureMode || _compare == null)
                    SelectElement(_selected);
                else
                {
                    var sections = InspectorServices.Current.Properties.Collect(_selected, GetRectInWindow(_selected));
                    _panelLayer.ShowSelection(_selected, sections, ParentChain(_selected), scrollTree: false);
                }
            }
        };
        _panelLayer.StructureMenuEdited += select =>
        {
            RefreshTree();
            if (select != null)
                SelectElement(select);
            else if (_selected != null)
                UpdateHighlight();
        };
        _panelLayer.SelectModeToggled += SetSelectMode;
        _panelLayer.MeasureModeToggled += SetMeasureMode;
        _panelLayer.DebugPaintToggled += SetDebugPaint;
        _panelLayer.CookbookRequested += () =>
        {
            // The gallery is a page of its own — long-press any tile to inspect it.
            Hide();
            _ = InspectorServices.Current.Cookbook.OpenAsync(null);
        };
        _panelLayer.ElementPicked += (el, scrollTree) =>
        {
            if (_measureMode && _selected != null)
                SetCompareElement(el);
            else
                SelectElement(el, scrollTree);
        };
        _panelLayer.DumpRequested += DumpHierarchy;
        _panelLayer.PropertyEdited += OnPropertyEdited;
        _panelLayer.StructureEdited += () =>
        {
            if (_selected is not { } element || _panelLayer == null)
                return;
            OnPropertyEdited();
            // Rebuild the sections (span/definition counts changed) keeping the scroll position.
            var sections = InspectorServices.Current.Properties.Collect(element, GetRectInWindow(element));
            _panelLayer.ShowSelection(element, sections, ParentChain(element), scrollTree: false, preservePropsScroll: true);
        };
        _panelLayer.ToolsDispatcher = _window.Dispatcher;
        _panelLayer.BottomInset = GetBottomInsetPlatform();
        _panelLayer.WindowSizeProvider = () => new Size(_window.Width, _window.Height);
        // Panel is parented on UIWindow/DecorView with a fixed native frame — MAUI
        // TranslationX/Y alone often does nothing there; platform applies the transform.
        _panelLayer.ApplyDragOffset = SetPanelOffsetPlatform;
    }

    Point LayerOrigin => GetLayerOriginPlatform();

    /// <summary>Roots in bottom-to-top order: the window page followed by any modal pages.</summary>
    IEnumerable<VisualElement> RootElements()
    {
        if (_window.Page is { } page)
        {
            yield return page;

            IReadOnlyList<Page>? modals = null;
            try { modals = page.Navigation?.ModalStack; }
            catch { /* navigation may be unavailable mid-teardown */ }

            if (modals != null)
                foreach (var modal in modals)
                    yield return modal;
        }
    }

    void RefreshTree() => _panelLayer?.SetTree(TreeNode.Build(RootElements()));

    /// <summary>Writes an indented, designer-oriented dump of the whole visual tree to the console.</summary>
    void DumpHierarchy()
    {
        var dump = HierarchyDumper.Dump(RootElements(), GetRectInWindow, new Size(_window.Width, _window.Height));
        foreach (var line in dump.Split('\n'))
            Console.WriteLine(line.TrimEnd('\r'));
    }

    /// <summary>Re-measures the highlight after a live property edit (twice: now and post-layout).</summary>
    void OnPropertyEdited()
    {
        if (_selected is not { } element)
            return;

        UpdateHighlight();
        _window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
        {
            if (IsShown && ReferenceEquals(_selected, element))
                UpdateHighlight();
        });
    }

    Rect? GetRectInWindow(VisualElement element) => GetRectInWindowPlatform(element);

    static List<VisualElement> ParentChain(VisualElement element)
    {
        var chain = new List<VisualElement>();
        for (Element? current = element; current != null; current = current.Parent)
        {
            // Skip non-visual intermediaries (ShellContent etc.) but keep walking up.
            if (current is VisualElement ve)
                chain.Add(ve);
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>Entry point from the platform long-press detectors; point is in window coordinates (dp).</summary>
    internal void OnLongPressDetected(Point windowPoint)
    {
        if (IsShown && _panelLayer != null)
            return;
        _window.Dispatcher.Dispatch(() => Show(windowPoint));
    }

    // Platform pieces:
    private partial void AttachPlatform();
    private partial void DetachPlatform();
    /// <summary>True when a real platform touch was injected at the window-dp point.</summary>
    private partial bool InjectTapPlatform(Point windowDp);

    private partial void AddLayersPlatform();
    private partial void RemoveLayersPlatform();
    private partial void SetPanelOffsetPlatform(double xDp, double yDp);
    private partial Rect? GetRectInWindowPlatform(VisualElement element);
    private partial Point GetLayerOriginPlatform();
    private partial double GetBottomInsetPlatform();
    private partial byte[]? CapturePngPlatform();
}
