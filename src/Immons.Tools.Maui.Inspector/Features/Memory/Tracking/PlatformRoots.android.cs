using Android.Views;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;
using Window = Microsoft.Maui.Controls.Window;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

internal static partial class PlatformRoots
{
    static partial void CollectHosted(Window window, List<Element> roots)
    {
        if (window.Handler?.PlatformView is not Android.App.Activity { Window.DecorView: ViewGroup decor })
            return;
        for (var i = 0; i < decor.ChildCount; i++)
        {
            if (decor.GetChildAt(i) is { } child && ToElement(child) is { } element)
                roots.Add(element);
        }
    }

    static partial void ReadOnScreen(Element element, ref bool? onScreen) =>
        onScreen = element.Handler?.PlatformView is AView { IsAttachedToWindow: true };

    static Element? ToElement(AView view) => view switch
    {
        WrapperView wrapper => wrapper.ChildCount > 0 && wrapper.GetChildAt(0) is { } inner ? ToElement(inner) : null,
        LayoutViewGroup layout => layout.CrossPlatformLayout as Element,
        ContentViewGroup content => content.CrossPlatformLayout as Element,
        _ => null,
    };
}
