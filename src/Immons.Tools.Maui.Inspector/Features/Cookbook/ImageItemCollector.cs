namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Every image bundled into the app package (MauiImage), by the name a FileImageSource takes.</summary>
internal static class ImageItemCollector
{
    public static void Collect(CookbookItemSink sink)
    {
        foreach (var name in ImageAssetCatalog.BundledImages())
        {
            // The source path (from the package's build targets) lets folders filter the section.
            var source = ImageSourceManifest.SourceOf(name);
            sink.Add(CookbookSections.Images, new CookbookItem("", "", name, CookbookKinds.Image,
                null, source ?? "app package", source ?? "bundled image", name, () => ResourceValueSample.Image(ImageSource.FromFile(name))));
        }
    }
}
