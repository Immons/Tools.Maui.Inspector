namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Built-in section ids, their titles and display order; recipe sections come from their keys.</summary>
internal static class CookbookSections
{
    public const string Colors = "colors";
    public const string Typography = "typography";
    public const string Styles = "styles";
    public const string Controls = "controls";
    public const string Templates = "templates";
    public const string Images = "images";
    public const string Scalars = "scalars";
    public const string Recipes = "recipes";

    /// <summary>Recipe sections are keyed "recipe-{name}" so they never clash with the built-ins.</summary>
    public const string RecipePrefix = "recipe-";

    public static readonly string[] Order =
        [Colors, Typography, Styles, Controls, Templates, Images, Scalars];

    public static string TitleOf(string id) => id switch
    {
        Colors => "Colors",
        Typography => "Typography",
        Styles => "Styles",
        Controls => "Controls",
        Templates => "Templates",
        Images => "Images",
        Scalars => "Scalars & shadows",
        Recipes => "Recipes",
        _ => id,
    };

    /// <summary>Minimum tile width in dp — swatches and scalars pack tighter than control samples.</summary>
    public static double TileWidth(string id) => id switch
    {
        Colors => 124,
        Scalars => 150,
        Images => 150,
        _ => 300,
    };
}
