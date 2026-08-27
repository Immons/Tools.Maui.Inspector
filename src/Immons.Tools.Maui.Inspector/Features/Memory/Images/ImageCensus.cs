namespace Immons.Tools.Maui.Inspector.Features.Memory.Images;

/// <summary>One decoded image in memory, attributed to the element showing it when there is one.</summary>
internal sealed record ImageInfo(string Owner, string Source, int Width, int Height, long Bytes, bool Attached);

internal sealed record ImageReport(bool Supported, long TotalBytes, IReadOnlyList<ImageInfo> Images);

/// <summary>
/// Decoded bitmaps — the usual native-memory hog on phones. The tracked Image / ImageButton
/// elements give the owner and the source; Android adds every Bitmap peer, the ones no element
/// shows any more included. iOS has no bitmap registry, so its list is the elements' images only.
/// </summary>
internal static partial class ImageCensus
{
    public static ImageReport Collect(ITrackedInstances instances)
    {
        // Live(), not Prune(): the census only looks — pruning would eat the next snapshot's collected tally.
        var elements = instances.Live()
            .Where(r => r.Kind == TrackedKind.Element)
            .Select(r => r.Target)
            .OfType<View>()
            .Where(v => v is Image or ImageButton)
            .Select(v => (Element: v, Attached: ElementAttachment.IsAttached(v)))
            .ToList();

        List<ImageInfo>? images = null;
        try
        {
            CollectPlatform(elements, ref images);
        }
        catch
        {
            images = null;
        }
        var list = (images ?? []).OrderByDescending(i => i.Bytes).ToList();
        return new ImageReport(images != null, list.Sum(i => i.Bytes), list);
    }

    static string Owner(View element)
    {
        var chain = ParentChain.Of(element, includeSelf: false);
        return ParentChain.Label(element) + (chain.Count > 0 ? " in " + chain[^1] : "");
    }

    static string Source(View element) => element switch
    {
        Image { Source: { } source } => Describe(source),
        ImageButton { Source: { } source } => Describe(source),
        _ => "",
    };

    static string Describe(ImageSource source) => source switch
    {
        FileImageSource file => file.File,
        UriImageSource uri => uri.Uri?.ToString() ?? "uri",
        StreamImageSource => "stream",
        FontImageSource font => "font glyph " + font.Glyph,
        _ => TypeNames.Short(source.GetType()),
    };

    static partial void CollectPlatform(List<(View Element, bool Attached)> elements, ref List<ImageInfo>? images);
}
