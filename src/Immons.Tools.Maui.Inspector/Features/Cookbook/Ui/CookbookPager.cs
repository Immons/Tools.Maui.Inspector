namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>"Styles · 47 items · page 2/3" with ‹ › — hidden while a section fits on one page.</summary>
internal sealed class CookbookPager : Grid
{
    readonly ChromeLabel _label = new()
    {
        TextColor = Theme.TextSecondary,
        FontSize = Theme.FontSize,
        VerticalOptions = LayoutOptions.Center,
        LineBreakMode = LineBreakMode.TailTruncation,
    };
    readonly ChromeButton _previous = CookbookChrome.Button("‹");
    readonly ChromeButton _next = CookbookChrome.Button("›");

    /// <summary>+1 / −1 relative to the current page.</summary>
    public event Action<int>? PageRequested;

    public CookbookPager()
    {
        BackgroundColor = Theme.PanelBg;
        Padding = new Thickness(10, 0, 10, 8);
        ColumnSpacing = 6;
        ColumnDefinitions =
        [
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
        ];
        _previous.Clicked += (_, _) => PageRequested?.Invoke(-1);
        _next.Clicked += (_, _) => PageRequested?.Invoke(1);
        this.Add(_label, 0);
        this.Add(_previous, 1);
        this.Add(_next, 2);
    }

    public void Update(string title, int page, int pages, int count)
    {
        _label.Text = pages > 1
            ? $"{title} · {count} items · page {page + 1}/{pages}"
            : $"{title} · {count} item{(count == 1 ? "" : "s")}";
        _previous.IsVisible = pages > 1;
        _next.IsVisible = pages > 1;
        _previous.IsEnabled = page > 0;
        _next.IsEnabled = page < pages - 1;
        _previous.Opacity = page > 0 ? 1 : 0.4;
        _next.Opacity = page < pages - 1 ? 1 : 0.4;
    }
}
