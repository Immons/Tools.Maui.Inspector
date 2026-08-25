namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Every font registered with ConfigureFonts, rendered as a type specimen.</summary>
internal static class FontItemCollector
{
    public static void Collect(CookbookItemSink sink)
    {
        foreach (var (alias, file) in FontCatalog.RegisteredFonts())
        {
            var detail = file.Length > 0 ? file : "registered font";
            sink.Add(CookbookSections.Typography, new CookbookItem("", "", alias, CookbookKinds.Font,
                null, file.Length > 0 ? file : null, detail, null, () => ResourceValueSample.Font(alias)));
        }
    }
}
