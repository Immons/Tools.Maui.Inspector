using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;
using Microsoft.Maui.Controls.Shapes;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Visual forms of value resources: swatches, shadows, insets, type specimens.</summary>
internal static class ResourceValueSample
{
    const double BoxWidth = 96;
    const double BoxHeight = 52;

    public static View Swatch(Brush brush) => CookbookChrome.Box(brush, BoxWidth, BoxHeight);

    /// <summary>Repaints a swatch from the resource's current value (a Color or a Brush).</summary>
    public static void Refresh(View swatch, object? current)
    {
        var brush = current switch
        {
            Color color => new SolidColorBrush(color),
            Brush b => b,
            _ => null,
        };
        if (brush != null)
            swatch.Background = brush;
    }

    public static View Shadow(Shadow shadow)
    {
        var card = CookbookChrome.Box(new SolidColorBrush(Colors.White), BoxWidth, BoxHeight);
        card.Shadow = shadow;
        card.Margin = new Thickness(8, 8, 8, 18);
        return card;
    }

    public static View Image(ImageSource source) => new ChromeImage
    {
        Source = source,
        HeightRequest = 80,
        Aspect = Aspect.AspectFit,
        HorizontalOptions = LayoutOptions.Start,
    };

    public static View Font(string alias)
    {
        var stack = new ChromeStack { Spacing = 2 };
        stack.Add(Specimen(alias, 24, "Aa Bb Cc"));
        stack.Add(Specimen(alias, 14, SampleContent.Text));
        stack.Add(Specimen(alias, 14, "0123456789 !?&@%"));
        return stack;
    }

    public static View Scalar(object value) => value switch
    {
        Thickness thickness => Insets(thickness),
        CornerRadius radius => CookbookChrome.Box(new SolidColorBrush(CookbookChrome.Fill), BoxWidth, BoxHeight, radius),
        _ => CookbookChrome.Label(ValueFormatter.FormatValue(value), 20),
    };

    /// <summary>The thickness as padding around a filled box — the insets become visible.</summary>
    static View Insets(Thickness thickness)
    {
        var inner = CookbookChrome.Box(new SolidColorBrush(CookbookChrome.Fill), 48, 24, new CornerRadius(3));
        return new ChromeBorder
        {
            Padding = thickness,
            Stroke = new SolidColorBrush(CookbookChrome.Stroke),
            StrokeThickness = 1,
            StrokeDashArray = [3, 2],
            StrokeShape = new Rectangle(),
            HorizontalOptions = LayoutOptions.Start,
            Content = inner,
        };
    }

    static ChromeLabel Specimen(string alias, double size, string text)
    {
        var label = CookbookChrome.Label(text, size);
        label.FontFamily = alias;
        label.LineBreakMode = LineBreakMode.WordWrap;
        return label;
    }
}
