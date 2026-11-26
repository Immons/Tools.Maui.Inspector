using System.Collections.ObjectModel;
using System.ComponentModel;
using Immons.Tools.Maui.Inspector.Inspector;

namespace Immons.Tools.Maui.Inspector.Features.VisualTree.Ui;

internal sealed class TreeNodeVM : INotifyPropertyChanged
{
    bool _isExpanded;
    bool _isSelected;

    public required TreeNode Node { get; init; }
    public required int Depth { get; init; }

    public string Label => Node.Label;
    public bool HasChildren => Node.Children.Count > 0;
    public string Chevron => HasChildren ? (IsExpanded ? "▾" : "▸") : "";
    public Thickness Indent => new(4 + Depth * 14, 0, 0, 0);
    public Color RowBackground => IsSelected ? Theme.RowSelected : Colors.Transparent;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Raise(nameof(Chevron)); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Raise(nameof(RowBackground)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Flattened, expandable visual tree list.</summary>
internal sealed class TreePane : Grid
{
    const double ChevronZoneWidth = 26;

    readonly ObservableCollection<TreeNodeVM> _items = [];
    readonly CollectionView _list;
    List<TreeNode> _roots = [];
    TreeNodeVM? _selected;

    public event Action<VisualElement>? Picked;

    /// <summary>Double-tap on a row: structural actions (add/wrap/move/remove).</summary>
    public event Action<VisualElement>? StructureRequested;

    public TreePane()
    {
        this.NoSafeArea();
        _list = new CollectionView
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(CreateRow),
            VerticalOptions = LayoutOptions.Fill,
        };
        Add(_list);
    }

    public void SetRoots(List<TreeNode> roots)
    {
        _roots = roots;
        _selected = null;
        _items.Clear();

        foreach (var root in roots)
        {
            var vm = new TreeNodeVM { Node = root, Depth = 0 };
            _items.Add(vm);
            // Auto-expand the chain of single children so the tree opens on something useful.
            while (vm.HasChildren)
            {
                Expand(vm);
                if (vm.Node.Children.Count != 1)
                    break;
                vm = _items[_items.IndexOf(vm) + 1];
            }
        }
    }

    public bool Contains(VisualElement element) => TreeNode.Find(_roots, element) != null;

    public void Select(VisualElement element, bool scroll)
    {
        var node = TreeNode.Find(_roots, element);
        if (node == null)
            return;

        // Expand ancestors from the root down.
        var ancestors = new List<TreeNode>();
        for (var p = node.Parent; p != null; p = p.Parent)
            ancestors.Add(p);
        ancestors.Reverse();

        foreach (var ancestor in ancestors)
        {
            if (FindVm(ancestor) is { IsExpanded: false } vm)
                Expand(vm);
        }

        if (FindVm(node) is not { } target)
            return;

        SetSelectedVm(target);

        if (scroll)
            _list.ScrollWhenReady(list => list.ScrollTo(target, position: ScrollToPosition.Center, animate: false));
    }

    TreeNodeVM? FindVm(TreeNode node) => _items.FirstOrDefault(vm => ReferenceEquals(vm.Node, node));

    void SetSelectedVm(TreeNodeVM vm)
    {
        if (_selected != null)
            _selected.IsSelected = false;
        _selected = vm;
        vm.IsSelected = true;
    }

    void Toggle(TreeNodeVM vm)
    {
        if (!vm.HasChildren)
            return;
        if (vm.IsExpanded)
            Collapse(vm);
        else
            Expand(vm);
    }

    void Expand(TreeNodeVM vm)
    {
        if (vm.IsExpanded || !vm.HasChildren)
            return;
        vm.IsExpanded = true;
        var index = _items.IndexOf(vm);
        for (var i = 0; i < vm.Node.Children.Count; i++)
            _items.Insert(index + 1 + i, new TreeNodeVM { Node = vm.Node.Children[i], Depth = vm.Depth + 1 });
    }

    void Collapse(TreeNodeVM vm)
    {
        vm.IsExpanded = false;
        var index = _items.IndexOf(vm);
        while (index + 1 < _items.Count && _items[index + 1].Depth > vm.Depth)
            _items.RemoveAt(index + 1);
    }

    View CreateRow()
    {
        var row = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            ],
            ColumnSpacing = 2,
            Padding = new Thickness(8, 0),
            MinimumHeightRequest = 26,
        }.NoSafeArea();
        row.SetBinding(BackgroundColorProperty, static (TreeNodeVM vm) => vm.RowBackground);

        var chevron = Theme.MakeLabel(size: Theme.FontSize, color: Theme.TextSecondary);
        chevron.WidthRequest = 18;
        chevron.HorizontalTextAlignment = TextAlignment.Center;
        chevron.SetBinding(Label.TextProperty, static (TreeNodeVM vm) => vm.Chevron);
        chevron.SetBinding(MarginProperty, static (TreeNodeVM vm) => vm.Indent);
        row.Add(chevron, 0);

        var label = Theme.MakeLabel(size: Theme.FontSize);
        label.SetBinding(Label.TextProperty, static (TreeNodeVM vm) => vm.Label);
        row.Add(label, 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (sender, e) =>
        {
            if (sender is not Grid g || g.BindingContext is not TreeNodeVM vm)
                return;

            // Taps on the chevron zone toggle expansion; anywhere else selects the node.
            var x = e.GetPosition(g)?.X ?? double.MaxValue;
            if (vm.HasChildren && x <= ChevronZoneWidth + vm.Indent.Left)
            {
                Toggle(vm);
            }
            else
            {
                SetSelectedVm(vm);
                Expand(vm);
                Picked?.Invoke(vm.Node.Element);
            }
        };
        row.GestureRecognizers.Add(tap);

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (sender, _) =>
        {
            if (sender is Grid g && g.BindingContext is TreeNodeVM vm)
                StructureRequested?.Invoke(vm.Node.Element);
        };
        row.GestureRecognizers.Add(doubleTap);

        return row;
    }
}
