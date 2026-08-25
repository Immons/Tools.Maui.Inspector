namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>Dark inspector-style header: title, theme override buttons, close, and one chip per section — one section shows at a time.</summary>
internal sealed class CookbookHeaderBar : Grid
{
    readonly Dictionary<string, ChromeButton> _chips = [];
    readonly ChromeButton _system = CookbookChrome.Button("⚙︎ System");
    readonly ChromeButton _light = CookbookChrome.Button("☀︎");
    readonly ChromeButton _dark = CookbookChrome.Button("☾");

    public event Action<string>? SectionRequested;
    public event Action? CloseRequested;

    public CookbookHeaderBar(IEnumerable<(string Id, string Title)> sections)
    {
        BackgroundColor = Theme.PanelBg;
        RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
        RowSpacing = 6;
        Padding = new Thickness(10, 6, 10, 8);

        var top = new ChromeGrid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            ],
        };
        top.Add(new ChromeLabel
        {
            Text = "📚 Cookbook",
            TextColor = Theme.TextPrimary,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        }, 0);

        _system.Clicked += (_, _) => SetTheme(AppThemeSwitch.System);
        _light.Clicked += (_, _) => SetTheme(AppThemeSwitch.Light);
        _dark.Clicked += (_, _) => SetTheme(AppThemeSwitch.Dark);
        var close = CookbookChrome.Button("✕");
        close.Clicked += (_, _) => CloseRequested?.Invoke();
        top.Add(_system, 2);
        top.Add(_light, 3);
        top.Add(_dark, 4);
        top.Add(close, 5);
        this.Add(top, 0, 0);

        var chips = new ChromeRow { Spacing = 6 };
        foreach (var (id, title) in sections)
        {
            var chip = CookbookChrome.Button(title);
            chip.Clicked += (_, _) => SectionRequested?.Invoke(id);
            _chips[id] = chip;
            chips.Add(chip);
        }
        this.Add(new ChromeScroll
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = chips,
        }, 0, 1);

        RefreshTheme();
    }

    public void MarkCurrent(string sectionId)
    {
        foreach (var (id, chip) in _chips)
            CookbookChrome.Paint(chip, id == sectionId);
    }

    public void RefreshTheme()
    {
        var current = AppThemeSwitch.Current;
        CookbookChrome.Paint(_system, current == AppThemeSwitch.System);
        CookbookChrome.Paint(_light, current == AppThemeSwitch.Light);
        CookbookChrome.Paint(_dark, current == AppThemeSwitch.Dark);
    }

    void SetTheme(string theme)
    {
        AppThemeSwitch.Set(theme);
        RefreshTheme();
    }
}
