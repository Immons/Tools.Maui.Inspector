namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Columns and tile width for a section at a given page width — shared by the device list and the headless stage.</summary>
internal static class CookbookGridMetrics
{
    public const double Spacing = 8;

    public static (int Span, double TileWidth) For(string sectionId, double pageWidth)
    {
        var minimum = CookbookSections.TileWidth(sectionId);
        if (pageWidth <= 0)
            return (1, minimum);
        var span = Math.Max(1, (int)((pageWidth - Spacing) / (minimum + Spacing)));
        return (span, Math.Floor((pageWidth - Spacing * (span + 1)) / span));
    }
}
