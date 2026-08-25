namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

internal static partial class ImageAssetCatalog
{
    // MauiImage outputs are copied next to the executable (name.png, name.scale-200.png).
    private static partial IEnumerable<string> BundledImagesPlatform()
    {
        var root = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            yield break;

        foreach (var file in Directory.EnumerateFiles(root))
        {
            var name = Path.GetFileName(file);
            if (IsImageFile(name) && !name.StartsWith("splash", StringComparison.OrdinalIgnoreCase))
                yield return Canonical(name);
        }
    }
}
