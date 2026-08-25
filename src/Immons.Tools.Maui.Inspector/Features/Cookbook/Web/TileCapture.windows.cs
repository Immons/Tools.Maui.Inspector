using WBitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using WBitmapEncoder = Windows.Graphics.Imaging.BitmapEncoder;
using WBitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using WDataReader = Windows.Storage.Streams.DataReader;
using WMemoryStream = Windows.Storage.Streams.InMemoryRandomAccessStream;
using WRenderTargetBitmap = Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap;
using WUIElement = Microsoft.UI.Xaml.UIElement;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

internal static partial class TileCapture
{
    private static async partial Task<byte[]?> CapturePlatformAsync(VisualElement view)
    {
        if (view.Handler?.PlatformView is not WUIElement element)
            return null;

        var bitmap = new WRenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var width = (uint)bitmap.PixelWidth;
        var height = (uint)bitmap.PixelHeight;
        if (width == 0 || height == 0)
            return null;

        var pixels = await bitmap.GetPixelsAsync();
        var bgra = new byte[pixels.Length];
        using (var pixelReader = WDataReader.FromBuffer(pixels))
        {
            pixelReader.ReadBytes(bgra);
        }

        using var stream = new WMemoryStream();
        var encoder = await WBitmapEncoder.CreateAsync(WBitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(WBitmapPixelFormat.Bgra8, WBitmapAlphaMode.Premultiplied, width, height, 96, 96, bgra);
        await encoder.FlushAsync();

        var png = new byte[(int)stream.Size];
        using var reader = new WDataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(png);
        return png;
    }
}
