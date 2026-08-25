namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// The opaque backdrop behind a sample: what the options say, else the app's implicit page
/// background, else the platform defaults — painted explicitly so captures never come out
/// transparent (a transparent page style gives none).
/// </summary>
internal static class CookbookBackdrop
{
    public static readonly Color DefaultDark = Color.FromArgb("#FF121212");

    public static void Paint(VisualElement target, CookbookOptions options)
    {
        if (options.Background != null)
        {
            target.Background = options.Background;
            return;
        }
        if (options.LightBackground != null || options.DarkBackground != null)
        {
            target.SetAppThemeColor(VisualElement.BackgroundColorProperty,
                options.LightBackground ?? Colors.White, options.DarkBackground ?? DefaultDark);
            return;
        }
        if (ImplicitPageBackground() is { } background)
        {
            switch (background)
            {
                case Color color when color.Alpha > 0:
                    target.BackgroundColor = color;
                    return;
                case Brush brush:
                    target.Background = brush;
                    return;
            }
        }
        target.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, DefaultDark);
    }

    /// <summary>A literal BackgroundColor/Background setter of the app's implicit ContentPage style, if any.</summary>
    static object? ImplicitPageBackground()
    {
        try
        {
            if (Application.Current?.Resources is not { } resources
                || !resources.TryGetValue(typeof(ContentPage).FullName!, out var value)
                || value is not Style style)
                return null;
            foreach (var setter in style.Setters)
            {
                if (setter.Property == VisualElement.BackgroundColorProperty || setter.Property == VisualElement.BackgroundProperty)
                    return setter.Value; // a theme binding here cannot be re-applied — the defaults take over
            }
        }
        catch
        {
            // a throwing dictionary just means the defaults
        }
        return null;
    }

    public static bool IsBlank(Color? color) => color == null || color.Alpha <= 0;
}
