using Android.Graphics;
using Android.Graphics.Drawables;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Images;

internal static partial class ImageCensus
{
    static partial void CollectPlatform(List<(View Element, bool Attached)> elements, ref List<ImageInfo>? images)
    {
        images = [];
        var seen = new HashSet<Bitmap>(ReferenceEqualityComparer.Instance);
        foreach (var (element, attached) in elements)
        {
            var drawable = (element.Handler?.PlatformView as Android.Widget.ImageView)?.Drawable;
            if (drawable is not BitmapDrawable { Bitmap: { IsRecycled: false } bitmap })
                continue;
            seen.Add(bitmap);
            images.Add(new ImageInfo(Owner(element), Source(element), bitmap.Width, bitmap.Height, bitmap.AllocationByteCount, attached));
        }

        // Every other bitmap the runtime still wraps: caches, drawables nobody shows, leaked views' images.
        foreach (var info in Java.Interop.JniEnvironment.Runtime.ValueManager.GetSurfacedPeers())
        {
            if (!info.SurfacedPeer.TryGetTarget(out var peer) || peer is not Bitmap { IsRecycled: false } bitmap || !seen.Add(bitmap))
                continue;
            images.Add(new ImageInfo("(bitmap not shown by a tracked element)", "", bitmap.Width, bitmap.Height, bitmap.AllocationByteCount, false));
        }
    }
}
