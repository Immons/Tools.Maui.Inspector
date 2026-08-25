using Microsoft.Maui.Controls.Shapes;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>Factories for the cookbook chrome — legible on the app's light and dark page backgrounds alike.</summary>
internal static class CookbookChrome
{
    public static readonly Color Stroke = Color.FromArgb("#66808080");
    public static readonly Color Fill = Color.FromArgb("#805C9EFF");
    public static readonly Color Warning = Color.FromArgb("#FFE8A33D");

    static readonly Color TextLight = Color.FromArgb("#FF1F1F1F");
    static readonly Color TextDark = Color.FromArgb("#FFEDEDED");
    static readonly Color CaptionLight = Color.FromArgb("#FF5F5F5F");
    static readonly Color CaptionDark = Color.FromArgb("#FFA8A8A8");

    public static ChromeLabel Label(string text, double size = 13, bool bold = false, bool caption = false)
    {
        var label = new ChromeLabel
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 2,
        };
        label.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty,
            caption ? CaptionLight : TextLight, caption ? CaptionDark : TextDark);
        return label;
    }

    public static ChromeBorder Box(Brush fill, double width, double height, CornerRadius? radius = null) => new()
    {
        Background = fill,
        WidthRequest = width,
        HeightRequest = height,
        Stroke = new SolidColorBrush(Stroke),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = radius ?? new CornerRadius(8) },
        HorizontalOptions = LayoutOptions.Start,
    };

    /// <summary>Header buttons wear the inspector's dark palette, like the on-device panel.</summary>
    public static ChromeButton Button(string text) => new()
    {
        Text = text,
        FontSize = 13,
        TextColor = Theme.TextPrimary,
        BackgroundColor = Theme.PanelBg2,
        Padding = new Thickness(10, 4),
        Margin = new Thickness(0),
        MinimumHeightRequest = 30,
        MinimumWidthRequest = 36,
        HeightRequest = 30,
        CornerRadius = 6,
        BorderWidth = 0,
        VerticalOptions = LayoutOptions.Center,
    };

    public static void Paint(ChromeButton button, bool active)
    {
        button.BackgroundColor = active ? Theme.Accent : Theme.PanelBg2;
        button.TextColor = active ? Colors.White : Theme.TextPrimary;
    }
}
