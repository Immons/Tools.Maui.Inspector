namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>
/// The gallery page: a real ContentPage pushed modally, so the samples get the app's implicit
/// styles, theme bindings and dynamic resources exactly as any other page would. One section
/// shows at a time, twenty tiles per page, in a virtualized list — switching swaps the
/// ItemsSource, nothing accumulates. (Web previews render headlessly — see CaptureStageHost.)
/// </summary>
internal sealed class CookbookPage : ContentPage
{
    const double Spacing = CookbookGridMetrics.Spacing;

    readonly CookbookHeaderBar _header;
    readonly CookbookPager _pager = new();
    readonly ChromeCollection _list = new() { SelectionMode = SelectionMode.None, Margin = new Thickness(Spacing, Spacing, Spacing, 0) };
    readonly Dictionary<string, CookbookTile> _realized = [];
    CookbookSection? _section;
    int _page;
    int _span = 1;
    double _tileWidth;

    public IReadOnlyList<CookbookSection> Catalog { get; }

    /// <summary>The data context every sample gets (CookbookOptions.BindingContext), null when none.</summary>
    public object? SampleContext { get; }

    public string? CurrentSection => _section?.Id;

    public int CurrentPage => _page;

    public int PageCount => _section == null ? 0 : CookbookPaging.PageCount(_section.Items.Count);

    public event Action? CloseRequested;

    /// <summary>A tile was tapped — the host opens the item on a page of its own.</summary>
    public event Action<CookbookItem>? FocusRequested;

    public CookbookPage(IReadOnlyList<CookbookSection> catalog, CookbookOptions options, object? sampleContext)
    {
        Catalog = catalog;
        SampleContext = sampleContext;
        Title = "Design cookbook";
        AdoptAppPageLook(options);

        _header = new CookbookHeaderBar(catalog.Select(s => (s.Id, s.Title)));
        _header.SectionRequested += id => ShowSection(id);
        _header.CloseRequested += () => CloseRequested?.Invoke();
        _pager.PageRequested += delta => ShowPage(_page + delta);

        _list.ItemTemplate = new DataTemplate(() => new CookbookTileCell(() => _tileWidth, () => SampleContext, OnRealized, OnRecycled));
        _list.ItemsLayout = Layout();

        var root = new ChromeGrid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
        };
        root.Add(_header, 0, 0);
        root.Add(_pager, 0, 1);
        root.Add(_list, 0, 2);
        Content = root;

        if (catalog.Count > 0)
            ShowSection(catalog[0].Id);
    }

    public CookbookItem? Find(string itemId) =>
        Catalog.SelectMany(s => s.Items).FirstOrDefault(i => i.Id == itemId);

    /// <summary>The tile as it is on screen right now; null when the list does not show the item.</summary>
    public CookbookTile? FindRealized(string itemId) => _realized.GetValueOrDefault(itemId);

    public bool ShowSection(string sectionId, int page = 0)
    {
        var section = Catalog.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return false;
        if (!ReferenceEquals(section, _section))
        {
            _section = section;
            Measure(Width);
            _list.ItemsLayout = Layout();
        }
        ShowPage(page);
        return true;
    }

    /// <summary>Shows the page holding the item and scrolls it into view (its tile realizes shortly after).</summary>
    public async Task<bool> ShowItemAsync(string itemId)
    {
        foreach (var section in Catalog)
        {
            var index = section.Items.ToList().FindIndex(i => i.Id == itemId);
            if (index < 0)
                continue;
            ShowSection(section.Id, CookbookPaging.PageOf(index));
            await Task.Delay(60);
            _list.ScrollTo(CookbookPaging.IndexOnPage(index), position: ScrollToPosition.MakeVisible, animate: false);
            return true;
        }
        return false;
    }

    public void RefreshTheme() => _header.RefreshTheme();

    /// <summary>Swatches copied their resource at build time — an edited entry is re-read into the ones on screen.</summary>
    public void RefreshSamples()
    {
        foreach (var tile in _realized.Values)
        {
            if (tile.Item.RefreshSample is { } refresh && tile.Normal is { } sample)
                refresh(sample);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || _section == null || !Measure(width))
            return;
        // A new column count (rotation, split view): re-lay out and rebuild the cells at the new width.
        _list.ItemsLayout = Layout();
        ApplyPage();
    }

    void ShowPage(int page)
    {
        if (_section == null)
            return;
        _page = Math.Clamp(page, 0, PageCount - 1);
        ApplyPage();
    }

    void ApplyPage()
    {
        if (_section == null)
            return;
        _realized.Clear();
        _list.ItemsSource = CookbookPaging.Slice(_section.Items, _page);
        _pager.Update(_section.Title, _page, PageCount, _section.Items.Count);
        _header.MarkCurrent(_section.Id);
    }

    /// <summary>Columns from the page width and the section's minimum tile width; true when they changed.</summary>
    bool Measure(double pageWidth)
    {
        var (span, tileWidth) = CookbookGridMetrics.For(_section?.Id ?? CookbookSections.Styles, pageWidth);
        var different = span != _span || Math.Abs(tileWidth - _tileWidth) > 0.5;
        _span = span;
        _tileWidth = tileWidth;
        return different;
    }

    GridItemsLayout Layout() => new(_span, ItemsLayoutOrientation.Vertical)
    {
        HorizontalItemSpacing = Spacing,
        VerticalItemSpacing = Spacing,
    };

    void OnRealized(CookbookTile tile)
    {
        _realized[tile.Item.Id] = tile;
        tile.AttachBackdrop(this);
        tile.Tapped += () => FocusRequested?.Invoke(tile.Item);
    }


    void OnRecycled(CookbookTile tile)
    {
        if (_realized.TryGetValue(tile.Item.Id, out var current) && ReferenceEquals(current, tile))
            _realized.Remove(tile.Item.Id);
    }

    /// <summary>
    /// The backdrop behind the samples: what the options say, else the app's implicit ContentPage
    /// style (the page then looks like every other one), else the platform defaults painted
    /// explicitly — captures need an opaque backdrop, and a transparent page style gives none.
    /// </summary>
    void AdoptAppPageLook(CookbookOptions options)
    {
        if (options.Background != null)
        {
            Background = options.Background;
            return;
        }
        if (options.LightBackground != null || options.DarkBackground != null)
        {
            this.SetAppThemeColor(BackgroundColorProperty, options.LightBackground ?? Colors.White, options.DarkBackground ?? CookbookBackdrop.DefaultDark);
            return;
        }

        if (Application.Current?.Resources is { } resources
            && resources.TryGetValue(typeof(ContentPage).FullName!, out var value)
            && value is Style implicitStyle
            && implicitStyle.TargetType.IsAssignableFrom(typeof(CookbookPage)))
            Style = implicitStyle;

        if (CookbookBackdrop.IsBlank(BackgroundColor) && Background == null)
            this.SetAppThemeColor(BackgroundColorProperty, Colors.White, CookbookBackdrop.DefaultDark);
    }
}
