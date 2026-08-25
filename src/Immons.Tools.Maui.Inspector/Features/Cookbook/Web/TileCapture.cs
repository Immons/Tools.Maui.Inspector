namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

/// <summary>Renders one tile's sample host to PNG — the web gallery's thumbnails. PNG: lossless, so
/// the client can compare bytes against a baseline.</summary>
internal static partial class TileCapture
{
    /// <summary>Main thread. Null when the view has no platform counterpart or no size yet.</summary>
    public static Task<byte[]?> CaptureAsync(VisualElement view)
    {
        if (view.Handler?.PlatformView == null || view.Width <= 0 || view.Height <= 0)
            return Task.FromResult<byte[]?>(null);
        try
        {
            return CapturePlatformAsync(view);
        }
        catch
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    private static partial Task<byte[]?> CapturePlatformAsync(VisualElement view);
}
