using Microsoft.Maui.Platform;
using WElement = Microsoft.UI.Xaml.FrameworkElement;
using WPanel = Microsoft.UI.Xaml.Controls.Panel;
using WTranslate = Microsoft.UI.Xaml.Media.TranslateTransform;
using WWindow = Microsoft.UI.Xaml.Window;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

internal sealed partial class CaptureStageHost
{
    WElement? _platform;

    // Translated far off screen; RenderTargetBitmap renders the element wherever it sits.
    private partial bool AttachPlatform(Window window, IMauiContext context)
    {
        if (window.Handler?.PlatformView is not WWindow platformWindow || platformWindow.Content is not WPanel root)
            return false;

        _platform = _stage.ToPlatform(context) as WElement;
        if (_platform == null)
            return false;
        _platform.IsHitTestVisible = false;
        _platform.RenderTransform = new WTranslate { X = -100000 };
        root.Children.Insert(0, _platform);
        return true;
    }
}
