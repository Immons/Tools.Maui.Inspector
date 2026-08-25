using Foundation;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

internal static partial class ImageAssetCatalog
{
    // MauiImage outputs land loose in the bundle root (name.png, name@2x.png, name@3x.png);
    // icons live in the asset catalog, the generated splash carries a hash — neither is listed.
    private static partial IEnumerable<string> BundledImagesPlatform()
    {
        var root = NSBundle.MainBundle.BundlePath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            yield break;

        foreach (var file in Directory.EnumerateFiles(root))
        {
            var name = Path.GetFileName(file);
            if (IsImageFile(name) && !IsGenerated(name))
                yield return Canonical(name);
        }
    }

    static bool IsGenerated(string name) =>
        name.StartsWith("AppIcon", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("splash_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("LaunchImage", StringComparison.OrdinalIgnoreCase);
}
