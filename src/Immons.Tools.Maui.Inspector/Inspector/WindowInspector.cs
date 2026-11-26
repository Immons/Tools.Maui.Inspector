

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
        InspectorServices.Current.VisualRoots.Changed += OnVisualRootsChanged;
    }

    /// <summary>
    /// An overlay hosted on the platform window was added above the inspector's own layers, or one
    /// went away: put the layers back on top and reload the tree.
    /// </summary>
    void OnVisualRootsChanged() => RehomeLayers();

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
        // The platform window is resolved for every activation mode — bounds, remote taps and
        // the web panel's highlight need it even when no gesture opens the on-device panel.
        if (_mauiContext != null)
            AttachPlatform(ActivationGate.GestureEnabled(_options.Activation));
    }

    public void Detach()
    {
        UnhookModalStack();
        _window.SizeChanged -= OnWindowSizeChanged;
        InspectorServices.Current.VisualRoots.Changed -= OnVisualRootsChanged;
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
            FireAndForget.Observe(InspectorServices.Current.Cookbook.OpenAsync(null), "cookbook open");
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

    /// <summary>
    /// Roots in bottom-to-top order: the window page, then the modal pages and the visual roots the
    /// page tree does not contain (overlays hosted on the platform window, roots the app reported),
    /// stacked as the platform stacks them.
    /// </summary>
    List<VisualElement> RootElements()
    {
        var roots = new List<VisualElement>();
        if (_window.Page is not { } page)
            return roots;
        roots.Add(page);

        var above = new List<VisualElement>();
        IReadOnlyList<Page>? modals = null;
        try { modals = page.Navigation?.ModalStack; }
        catch { /* navigation may be unavailable mid-teardown */ }
        if (modals != null)
            above.AddRange(modals);
        above.AddRange(OverlayRoots());
        roots.AddRange(InStackingOrder(above));
        return roots;
    }

    /// <summary>
    /// The tracker's visual roots (0.10.3 registry) that belong to this window and are on screen —
    /// re-scanned here, so the tree is current even with tracking off. A root inside another root
    /// or inside the page's own tree is not a root of the element tree.
    /// </summary>
    List<VisualElement> OverlayRoots()
    {
        var services = InspectorServices.Current;
        services.Tracker.RefreshRoots();
        var registry = services.VisualRoots;
        var candidates = new List<VisualElement>();
        foreach (var root in registry.Shown().Concat(registry.Explicit()).OfType<VisualElement>())
        {
            if (root.GetType().Assembly != typeof(WindowInspector).Assembly
                && !candidates.Contains(root)
                && BelongsHere(root)
                && !ElementAttachment.InWindowContent(root)
                && PlatformRoots.IsOnScreen(root) != false)
                candidates.Add(root);
        }
        return candidates.Where(root => !HasAncestorIn(root, candidates)).ToList();
    }

    bool BelongsHere(VisualElement root)
    {
        if (root.Window is { } window)
            return ReferenceEquals(window, _window);
        if (PlatformRoots.ZOrder(_window, root) is not null)
            return true;
        return ReferenceEquals(Application.Current?.Windows.FirstOrDefault(), _window);
    }

    static bool HasAncestorIn(Element element, List<VisualElement> roots)
    {
        for (var parent = element.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is VisualElement visual && roots.Contains(visual))
                return true;
        }
        return false;
    }

    /// <summary>Modals and overlays by platform stacking; their list order where the platform cannot tell.</summary>
    List<VisualElement> InStackingOrder(List<VisualElement> above)
    {
        if (above.Count < 2)
            return above;
        var keyed = above.Select(root => (Root: root, Z: PlatformRoots.ZOrder(_window, root))).ToList();
        return keyed.Any(k => k.Z is null)
            ? above
            : keyed.OrderBy(k => k.Z!.Value).Select(k => k.Root).ToList();
    }

    string? OverlayNote(VisualElement root)
    {
        if (root is Page)
            return null;
        var registry = InspectorServices.Current.VisualRoots;
        if (registry.IsExplicit(root))
            return "reported root";
        return registry.IsDiscovered(root) ? "window overlay" : null;
    }

    void RefreshTree() => _panelLayer?.SetTree(TreeNode.Build(RootElements(), OverlayNote));

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
        // Second line of defence: a detector that outlived a configuration without the gesture
        // (or a stray recognizer) must never open the panel under Activation.None.
        if (!ActivationGate.GestureEnabled(_options.Activation))
            return;
        if (IsShown && _panelLayer != null)
            return;
        _window.Dispatcher.Dispatch(() => Show(windowPoint));
    }

    // Platform pieces:
    /// <summary>Resolves the platform window; installs the activation gesture only when asked.</summary>
    private partial void AttachPlatform(bool activationGesture);
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
