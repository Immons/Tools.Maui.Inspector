namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Bundled image assets (MauiImage output) by the file name a FileImageSource takes.</summary>
internal static partial class ImageAssetCatalog
{
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    public static IReadOnlyList<string> BundledImages()
    {
        try
        {
            return BundledImagesPlatform()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return []; // an unreadable bundle just means an empty Images section
        }
    }

    static bool IsImageFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>"logo@2x.png" / "logo.scale-200.png" → "logo.png": one entry per asset, not per density.</summary>
    static string Canonical(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var at = name.IndexOf('@');
        if (at > 0)
            name = name[..at];
        var scale = name.IndexOf(".scale-", StringComparison.OrdinalIgnoreCase);
        if (scale > 0)
            name = name[..scale];
        return name + Path.GetExtension(fileName).ToLowerInvariant();
    }

    private static partial IEnumerable<string> BundledImagesPlatform();
}
