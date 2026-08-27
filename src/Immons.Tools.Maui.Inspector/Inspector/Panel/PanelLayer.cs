using Microsoft.Maui.Controls.Shapes;

namespace Immons.Tools.Maui.Inspector.Inspector.Panel;

/// <summary>
/// Bottom docked inspector panel composing the drag handle, <see cref="PanelHeaderBar"/>,
/// <see cref="BreadcrumbBar"/> and the Tree / Properties panes.
/// </summary>
internal sealed class PanelLayer : Border
{
    readonly PanelHeaderBar _header;
    readonly PanelToolsBar _tools;
    readonly MemoryPane _memoryPane;
    bool _treeTab;
    readonly BreadcrumbBar _breadcrumb;
    readonly PanelDragController _drag;
    readonly TreePane _treePane;
    readonly Features.Structure.Ui.StructureMenuPane _structureMenu = new();
    readonly PropertiesPane _propsPane;
    readonly Grid _root;

    public event Action? CloseRequested;
    public event Action? RefreshRequested;

    /// <summary>A structural edit was applied from the on-device menu; arg = element to select.</summary>
    public event Action<VisualElement?>? StructureMenuEdited;
    public event Action? PropertyEdited;
    public event Action? StructureEdited;
    public event Action? DumpRequested;
    public event Action<bool>? SelectModeToggled;
    public event Action<bool>? MeasureModeToggled;
    public event Action<bool>? DebugPaintToggled;
    public event Action? CookbookRequested;

    /// <summary>(element, scrollTree) — scrollTree is false when picked from the tree itself.</summary>
    public event Action<VisualElement, bool>? ElementPicked;

    /// <summary>Window size in dp, used to clamp panel dragging. Set by the inspector.</summary>
    public Func<Size>? WindowSizeProvider
    {
        get => _drag.WindowSizeProvider;
        set => _drag.WindowSizeProvider = value;
    }

    /// <summary>Applies the drag offset on the host platform view (frame/transform).</summary>
    public Action<double, double>? ApplyDragOffset
    {
        get => _drag.ApplyDragOffset;
        set => _drag.ApplyDragOffset = value;
    }

    public double DragOffsetX => _drag.OffsetX;

    public double DragOffsetY => _drag.OffsetY;

    public PanelLayer()
    {
        Background = new SolidColorBrush(Theme.PanelBg);
        Stroke = new SolidColorBrush(Theme.Divider);
        StrokeThickness = 1;
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14, 14, 0, 0) };
        Padding = new Thickness(0);

        _drag = new PanelDragController(this);

        _header = new PanelHeaderBar();
        _header.SelectModeToggled += on => SelectModeToggled?.Invoke(on);
        _header.MeasureModeToggled += on => MeasureModeToggled?.Invoke(on);
        _header.TabChanged += ShowTab;
        _header.DumpRequested += () => DumpRequested?.Invoke();
        _header.RefreshRequested += () => RefreshRequested?.Invoke();
        _header.CloseRequested += () => CloseRequested?.Invoke();
        _header.MoreRequested += () => _tools.IsVisible = !_tools.IsVisible;

        _tools = new PanelToolsBar { IsVisible = false };
        _tools.DebugPaintToggled += on => DebugPaintToggled?.Invoke(on);
        _tools.CookbookRequested += () => CookbookRequested?.Invoke();
        _tools.MemoryRequested += ToggleMemoryPane;

        _breadcrumb = new BreadcrumbBar();
        _breadcrumb.Picked += el => ElementPicked?.Invoke(el, true);

        _treePane = new TreePane { IsVisible = false };
        _treePane.Picked += el => ElementPicked?.Invoke(el, false);
        _treePane.StructureRequested += el => _structureMenu.Show(el);
        _propsPane = new PropertiesPane();
        _propsPane.Edited += () => PropertyEdited?.Invoke();
        _propsPane.StructureChanged += () => StructureEdited?.Invoke();

        _memoryPane = new MemoryPane { IsVisible = false };

        var contentHost = new Grid().NoSafeArea();
        contentHost.Add(_treePane);
        contentHost.Add(_propsPane);
        contentHost.Add(_memoryPane);

        var dragHandle = BuildDragHandle();

        _root = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            ],
        }.NoSafeArea();
        _root.Add(dragHandle, 0, 0);
        _root.Add(_header, 0, 1);
        _root.Add(_tools, 0, 2);
        _root.Add(_breadcrumb, 0, 3);
        _root.Add(contentHost, 0, 4);

        _structureMenu.Edited += select => StructureMenuEdited?.Invoke(select);
        var host = new Grid().NoSafeArea();
        host.Add(_root);
        host.Add(_structureMenu);
        Content = host;

        // Drag surfaces: handle, header spacer and the breadcrumb bar (labels still get taps).
        // Full-header pan would fight button presses on some platforms.
        _drag.Attach(dragHandle);
        _drag.Attach(_header.DragSpacer);
        _drag.Attach(_breadcrumb);

        ShowTab(tree: false);
    }

    /// <summary>Lets the tools bar refresh its fps readout.</summary>
    public IDispatcher? ToolsDispatcher
    {
        set => _tools.Dispatcher = value;
    }

    public double BottomInset
    {
        set => _root.Padding = new Thickness(0, 0, 0, value);
    }

    public void SetSelectModeVisual(bool on) => _header.SetSelectModeVisual(on);

    public void SetMeasureModeVisual(bool on) => _header.SetMeasureModeVisual(on);

    public void SetTree(List<TreeNode> roots) => _treePane.SetRoots(roots);

    public bool TreeContains(VisualElement element) => _treePane.Contains(element);

    public void ShowSelection(VisualElement element, List<PropertySection> sections, List<VisualElement> parentChain, bool scrollTree, bool preservePropsScroll = false)
    {
        _propsPane.Show(element, sections, preservePropsScroll);
        _breadcrumb.Update(parentChain);
        _treePane.Select(element, scrollTree);
    }

    void ShowTab(bool tree)
    {
        _treeTab = tree;
        _memoryPane.IsVisible = false;
        _treePane.IsVisible = tree;
        _propsPane.IsVisible = !tree;
        _header.SetTabVisual(tree);
    }

    /// <summary>The Memory pane takes the content area; a tab brings the tree / properties back.</summary>
    void ToggleMemoryPane()
    {
        var show = !_memoryPane.IsVisible;
        _propsPane.IsVisible = !show && !_treeTab;
        _treePane.IsVisible = !show && _treeTab;
        _memoryPane.IsVisible = show;
        if (show)
            _memoryPane.Render();
    }

    static Grid BuildDragHandle()
    {
        var pill = new BoxView
        {
            Color = Theme.TextSecondary,
            Opacity = 0.7,
            CornerRadius = 2.5,
            WidthRequest = 40,
            HeightRequest = 5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        return new Grid
        {
            HeightRequest = 28,
            // Must be opaque enough to receive hits (iOS ignores near-zero alpha views).
            BackgroundColor = Theme.PanelBg,
            Children = { pill },
        }.NoSafeArea();
    }
}
