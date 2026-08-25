using AView = Android.Views.View;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

internal static partial class TileCapture
{
    // Software draw of the view subtree — works for views outside the visible scroll region.
    private static partial Task<byte[]?> CapturePlatformAsync(VisualElement view)
    {
        if (view.Handler?.PlatformView is not AView native
            || !native.IsAttachedToWindow || native.Width <= 0 || native.Height <= 0)
            return Task.FromResult<byte[]?>(null);

        var bitmap = Android.Graphics.Bitmap.CreateBitmap(native.Width, native.Height, Android.Graphics.Bitmap.Config.Argb8888!);
        var canvas = new Android.Graphics.Canvas(bitmap);
        native.Draw(canvas);

        return Task.Run(() =>
        {
            using (bitmap)
            {
                using var stream = new MemoryStream();
                bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png!, 100, stream);
                return (byte[]?)stream.ToArray();
            }
        });
    }
}
