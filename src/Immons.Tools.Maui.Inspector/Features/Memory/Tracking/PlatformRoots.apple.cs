using Microsoft.Maui.Platform;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

internal static partial class PlatformRoots
{
    static partial void CollectHosted(Window window, List<Element> roots)
    {
        if (window.Handler?.PlatformView is not UIWindow platformWindow)
            return;
        foreach (var subview in platformWindow.Subviews)
        {
            if (ToElement(subview) is { } element)
                roots.Add(element);
        }
    }

    static partial void ReadOnScreen(Element element, ref bool? onScreen) =>
        onScreen = element.Handler?.PlatformView is UIView { Window: not null };

    static Element? ToElement(UIView view) => view switch
    {
        WrapperView wrapper => wrapper.Subviews.Length > 0 ? ToElement(wrapper.Subviews[0]) : null,
        MauiView maui => maui.View as Element ?? maui.CrossPlatformLayout as Element,
        _ => null,
    };
}
