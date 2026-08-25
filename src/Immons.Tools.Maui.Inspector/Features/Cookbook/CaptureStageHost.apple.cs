using CoreGraphics;
using Microsoft.Maui.Platform;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

internal sealed partial class CaptureStageHost
{
    UIView? _platform;

    // Off screen rather than hidden: hidden views do not snapshot, off-screen ones do.
    private partial bool AttachPlatform(Window window, IMauiContext context)
    {
        if (window.Handler?.PlatformView is not UIWindow uiWindow)
            return false;

        _platform = _stage.ToPlatform(context);
        var bounds = uiWindow.Bounds;
        _platform.Frame = new CGRect(-bounds.Width * 3, 0, bounds.Width, bounds.Height);
        _platform.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        _platform.UserInteractionEnabled = false;
        uiWindow.InsertSubview(_platform, 0);
        return true;
    }
}
