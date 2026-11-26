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
        onScreen = Live(element) is { Window: not null };

    static partial void ReadZOrder(Window window, Element root, ref int? z)
    {
        if (window.Handler?.PlatformView is not UIWindow platformWindow || Live(root) is not { } view
            || !ReferenceEquals(view.Window, platformWindow))
            return;
        while (view.Superview is { } superview && !ReferenceEquals(superview, platformWindow))
            view = superview;
        var index = Array.IndexOf(platformWindow.Subviews, view);
        z = index >= 0 ? index : null;
    }

    /// <summary>The element's platform view while the native object is alive (a disposed NSObject throws on any member).</summary>
    static UIView? Live(Element element) =>
        element.Handler?.PlatformView is UIView { Handle: var handle } view && handle != IntPtr.Zero ? view : null;

    static Element? ToElement(UIView view) => view switch
    {
        WrapperView wrapper => wrapper.Subviews.Length > 0 ? ToElement(wrapper.Subviews[0]) : null,
        MauiView maui => maui.View as Element ?? maui.CrossPlatformLayout as Element,
        _ => null,
    };
}
