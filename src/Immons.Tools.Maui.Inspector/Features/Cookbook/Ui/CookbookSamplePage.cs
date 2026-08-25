namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>
/// One sample on a page of its own: laid out at the full page width — or at the width the
/// control declares. ▤ opens its property sheet underneath on demand (own properties first,
/// inherited ones folded), edited live. Opened by tapping a tile, or from the web panel.
/// </summary>
internal sealed class CookbookSamplePage : ContentPage, IFocusedSample
{
    const double SampleShareWithSheet = 0.45;

    readonly ChromeScroll _sampleScroll;
    readonly ChromeGrid _sheet = new() { BackgroundColor = Theme.PanelBg, IsVisible = false };
    readonly ChromeGrid _root;
    readonly ChromeButton _sheetToggle = CookbookChrome.Button("▤");
    readonly PropertiesPane _pane = new();
    double _height;
    bool _sheetBuilt;

    public CookbookItem Item { get; }

    /// <summary>The element the web captures — the sample alone, on the page backdrop.</summary>
    public SampleHost Host { get; } = new() { Padding = new Thickness(12), VerticalOptions = LayoutOptions.Start };

    public View? Sample { get; private set; }

    public string? Error { get; private set; }

    public event Action? CloseRequested;

    public CookbookSamplePage(CookbookItem item, object? sampleContext, CookbookPage owner)
    {
        Item = item;
        Title = item.Name;
        // The same backdrop as the gallery page (options, implicit page style or the defaults).
        SetBinding(BackgroundColorProperty, new Binding(nameof(BackgroundColor), source: owner));
        SetBinding(BackgroundProperty, new Binding(nameof(Background), source: owner));
        Host.SetBinding(BackgroundColorProperty, new Binding(nameof(BackgroundColor), source: this));
        Host.SetBinding(BackgroundProperty, new Binding(nameof(Background), source: this));
        Host.BindingContext = sampleContext;

        BuildSample();

        _sampleScroll = new ChromeScroll { Content = Host };
        _sheet.Add(_pane);
        _pane.StructureChanged += ShowProperties;

        _root = new ChromeGrid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            ],
        };
        _root.Add(Header(item), 0, 0);
        _root.Add(_sampleScroll, 0, 1);
        _root.Add(_sheet, 0, 2);
        Content = _root;
    }

    /// <summary>Whether the property sheet is open under the sample.</summary>
    public bool SheetShown => _sheet.IsVisible;

    /// <summary>Shows or hides the sheet: the sample keeps the whole screen until it is asked for.</summary>
    public void ShowSheet(bool shown)
    {
        if (shown && !_sheetBuilt)
        {
            _sheetBuilt = true;
            ShowProperties();
        }
        _sheet.IsVisible = shown;
        _root.RowDefinitions[1].Height = shown ? GridLength.Auto : GridLength.Star;
        _root.RowDefinitions[2].Height = shown ? GridLength.Star : GridLength.Auto;
        ApplySampleHeight();
        CookbookChrome.Paint(_sheetToggle, shown);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (height <= 0 || Math.Abs(height - _height) < 0.5)
            return;
        _height = height;
        ApplySampleHeight();
    }

    /// <summary>With the sheet open the sample keeps the upper part of the screen (a tall one scrolls there).</summary>
    void ApplySampleHeight() =>
        _sampleScroll.MaximumHeightRequest = _sheet.IsVisible && _height > 0
            ? Math.Floor(_height * SampleShareWithSheet)
            : double.PositiveInfinity; // "no cap" — a negative request would collapse the sample

    void BuildSample()
    {
        try
        {
            Sample = Item.CreateSample?.Invoke();
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }

        if (Sample != null)
        {
            if (CookbookKinds.IsCentered(Item.Kind))
            {
                Sample.HorizontalOptions = LayoutOptions.Center;
                Sample.VerticalOptions = LayoutOptions.Center;
            }
            Host.Add(Sample);
            return;
        }
        Host.Add(new ChromeLabel
        {
            Text = Error != null ? "⚠ " + Error : "no visual form — setters only",
            FontSize = 12,
            TextColor = Error != null ? CookbookChrome.Warning : Theme.TextSecondary,
            LineBreakMode = LineBreakMode.WordWrap,
        });
    }

    void ShowProperties()
    {
        if (Sample is not { } sample || !_sheetBuilt)
            return;
        _pane.Show(sample, CookbookPropertySections.Arrange(
            InspectorServices.Current.Properties.Collect(sample, MauiInspector.ActiveInspector?.BoundsOf(sample))),
            preserveScroll: true);
    }

    View Header(CookbookItem item)
    {
        var back = CookbookChrome.Button("‹ Cookbook");
        back.Clicked += (_, _) => CloseRequested?.Invoke();

        var titles = new ChromeStack { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        titles.Add(new ChromeLabel
        {
            Text = item.Name,
            TextColor = Theme.TextPrimary,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
        });
        if (!string.IsNullOrEmpty(item.Detail))
        {
            titles.Add(new ChromeLabel
            {
                Text = item.Detail,
                TextColor = Theme.TextSecondary,
                FontSize = Theme.FontSizeSmall,
                LineBreakMode = LineBreakMode.TailTruncation,
            });
        }

        _sheetToggle.Clicked += (_, _) => ShowSheet(!SheetShown);

        var header = new ChromeGrid
        {
            BackgroundColor = Theme.PanelBg,
            Padding = new Thickness(10, 6, 10, 8),
            ColumnSpacing = 10,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
        };
        header.Add(back, 0);
        header.Add(titles, 1);
        header.Add(_sheetToggle, 2);
        return header;
    }
}
