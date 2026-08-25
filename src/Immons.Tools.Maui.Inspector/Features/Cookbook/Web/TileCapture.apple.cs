using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

internal static partial class TileCapture
{
    // Snapshot on the UI thread (afterScreenUpdates renders views the scroll view has not
    // brought on screen yet); PNG encoding — the expensive part — on the pool.
    private static partial Task<byte[]?> CapturePlatformAsync(VisualElement view)
    {
        if (view.Handler?.PlatformView is not UIView native || native.Window == null || native.Bounds.Width <= 0)
            return Task.FromResult<byte[]?>(null);

        var format = UIGraphicsImageRendererFormat.DefaultFormat;
        var scale = native.TraitCollection.DisplayScale;
        format.Scale = scale > 0 ? scale : UIScreen.MainScreen.Scale;
        format.Opaque = false;
        using var renderer = new UIGraphicsImageRenderer(native.Bounds.Size, format);
        var image = renderer.CreateImage(_ => native.DrawViewHierarchy(native.Bounds, true));

        return Task.Run(() =>
        {
            using (image)
            {
                return image.AsPNG()?.ToArray();
            }
        });
    }
}
