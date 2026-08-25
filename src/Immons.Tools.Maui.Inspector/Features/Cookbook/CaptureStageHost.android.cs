using Microsoft.Maui.Platform;
using AView = Android.Views.View;
using AViewGroup = Android.Views.ViewGroup;
using AViewStates = Android.Views.ViewStates;
using AFrameLayout = Android.Widget.FrameLayout;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

internal sealed partial class CaptureStageHost
{
    AView? _platform;

    // Invisible: laid out like any child of the decor, never drawn by the window — View.Draw still renders it.
    private partial bool AttachPlatform(Microsoft.Maui.Controls.Window window, IMauiContext context)
    {
        if (window.Handler?.PlatformView is not Android.App.Activity activity
            || activity.Window?.DecorView is not AViewGroup decor)
            return false;

        _platform = _stage.ToPlatform(context);
        _platform.Visibility = AViewStates.Invisible;
        decor.AddView(_platform, 0, new AFrameLayout.LayoutParams(
            AViewGroup.LayoutParams.MatchParent, AViewGroup.LayoutParams.MatchParent));
        return true;
    }
}
