namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>What a cookbook item is — the web client picks badges and rendering by kind.</summary>
internal static class CookbookKinds
{
    public const string Color = "color";
    public const string Brush = "brush";
    public const string Font = "font";
    /// <summary>A keyed (x:Key) style rendered on an instance of its TargetType.</summary>
    public const string Style = "style";
    /// <summary>An implicit style whose TargetType cannot be instantiated in a page (pages, Shell) — setters only.</summary>
    public const string Implicit = "implicit";
    /// <summary>A MAUI control with its implicit look.</summary>
    public const string Control = "control";
    /// <summary>One of the app's own controls.</summary>
    public const string Custom = "custom";
    public const string ControlTemplate = "controltemplate";
    public const string DataTemplate = "datatemplate";
    /// <summary>A DataTemplate keyed "Cookbook.Section.Name" — a sample authored in XAML.</summary>
    public const string Recipe = "recipe";
    public const string Image = "image";
    public const string Scalar = "scalar";
    public const string Shadow = "shadow";

    /// <summary>Small, fixed-size samples sit centered in their tile — a swatch or an insets box
    /// hugging the top-left corner reads like a margin that is not there.</summary>
    public static bool IsCentered(string kind) =>
        kind is Color or Brush or Scalar or Shadow or Image;
}
