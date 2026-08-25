namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// The recipe convention: a DataTemplate keyed "Cookbook.Section.Name" (or "Cookbook.Name")
/// is a sample authored in XAML — real content, hot-reloadable, editable with write-back.
/// </summary>
internal static class RecipeKey
{
    public const string Prefix = "Cookbook.";

    public static bool TryParse(string key, out string sectionId, out string sectionTitle, out string name)
    {
        sectionId = "";
        sectionTitle = "";
        name = "";
        if (!key.StartsWith(Prefix, StringComparison.Ordinal) || key.Length <= Prefix.Length)
            return false;

        var rest = key[Prefix.Length..];
        var dot = rest.IndexOf('.');
        if (dot <= 0 || dot == rest.Length - 1)
        {
            sectionId = CookbookSections.Recipes;
            sectionTitle = CookbookSections.TitleOf(CookbookSections.Recipes);
            name = rest;
            return true;
        }

        sectionTitle = rest[..dot];
        name = rest[(dot + 1)..];
        sectionId = CookbookSections.RecipePrefix + sectionTitle.ToLowerInvariant();
        return true;
    }
}
