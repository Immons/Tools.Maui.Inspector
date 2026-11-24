namespace Immons.Tools.Maui.Inspector.Features.Properties.Ui;

/// <summary>
/// Property sheet for the selected element, grouped into sections.
/// Row value views come from <see cref="PropertyRowViewFactory"/>.
/// </summary>
internal sealed class PropertiesPane : ScrollView
{
    readonly VerticalStackLayout _stack = new VerticalStackLayout
    {
        Padding = new Thickness(0, 0, 0, 10),
        Spacing = 0,
    }.NoSafeArea();

    readonly List<(View View, string? Group)> _allViews = [];
    readonly Dictionary<string, bool> _groupExpanded = [];
    readonly PropertyRowViewFactory _rowViews;

    VisualElement? _element;

    /// <summary>Raised after a property value was successfully applied.</summary>
    public event Action? Edited;

    /// <summary>Raised after an action row ran (structure changed — caller should rebuild the sections).</summary>
    public event Action? StructureChanged;

    public PropertiesPane()
    {
        Content = _stack;
        _rowViews = new PropertyRowViewFactory(
            () => _element,
            () => Edited?.Invoke(),
            () => StructureChanged?.Invoke(),
            group => _groupExpanded.TryGetValue(group, out var e) && e,
            ToggleGroup);
    }

    public void Show(VisualElement element, List<PropertySection> sections, bool preserveScroll = false)
    {
        _element = element;
        var scrollY = ScrollY;
        _allViews.Clear();

        foreach (var section in sections)
            AddSection(section);

        RebuildStack();

        if (preserveScroll)
            Dispatcher.Dispatch(() => FireAndForget.Observe(ScrollToAsync(0, scrollY, false), "properties scroll"));
        else
            FireAndForget.Observe(ScrollToAsync(0, 0, false), "properties scroll");
    }

    void AddSection(PropertySection section)
    {
        var header = Theme.MakeLabel(section.Title.ToUpperInvariant(), Theme.Accent, Theme.FontSizeSmall, bold: true);
        header.Padding = new Thickness(12, 10, 12, 2);
        _allViews.Add((header, section.Group));

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            ],
            ColumnSpacing = 12,
            RowSpacing = 3,
            Padding = new Thickness(12, 2, 12, 6),
        }.NoSafeArea();

        for (var i = 0; i < section.Rows.Count; i++)
        {
            var row = section.Rows[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var name = Theme.MakeLabel(row.Name, Theme.TextSecondary);
            name.VerticalOptions = LayoutOptions.Center;
            grid.Add(name, 0, i);

            grid.Add(_rowViews.Create(row, section.Title), 1, i);
        }

        _allViews.Add((grid, section.Group));

        var divider = new BoxView { Color = Theme.Divider, HeightRequest = 1, Margin = new Thickness(12, 2) };
        _allViews.Add((divider, section.Group));
    }

    /// <summary>Collapsed groups are physically removed from the stack (IsVisible toggling
    /// leaves the ScrollView content size stale on Android).</summary>
    void RebuildStack()
    {
        _stack.Clear();
        foreach (var (view, group) in _allViews)
        {
            if (group == null || (_groupExpanded.TryGetValue(group, out var e) && e))
                _stack.Add(view);
        }
    }

    void ToggleGroup(string group)
    {
        var expanded = !(_groupExpanded.TryGetValue(group, out var e) && e);
        _groupExpanded[group] = expanded;
        RebuildStack();
    }
}
