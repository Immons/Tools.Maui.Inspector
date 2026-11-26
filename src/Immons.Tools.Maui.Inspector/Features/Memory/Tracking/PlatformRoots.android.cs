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
        onScreen = Live(element) is { IsAttachedToWindow: true };

    const int DialogWindows = 10_000;

    static partial void ReadZOrder(Window window, Element root, ref int? z)
    {
        if (window.Handler?.PlatformView is not Android.App.Activity { Window.DecorView: ViewGroup decor }
            || Live(root) is not { IsAttachedToWindow: true } view)
            return;
        if (!ReferenceEquals(view.RootView, decor))
        {
            z = DialogWindows;
            return;
        }
        while (view.Parent is AView parent && !ReferenceEquals(parent, decor))
            view = parent;
        var index = decor.IndexOfChild(view);
        z = index >= 0 ? index : null;
    }

    /// <summary>
    /// The element's platform view while its Java peer is alive. A popup that closed leaves its handler pointing at a
    /// disposed view (Handle zero), and touching any of its members throws ObjectDisposedException.
    /// </summary>
    static AView? Live(Element element) =>
        element.Handler?.PlatformView is AView { Handle: var handle } view && handle != IntPtr.Zero ? view : null;

    static Element? ToElement(AView view) => view switch
    {
        WrapperView wrapper => wrapper.ChildCount > 0 && wrapper.GetChildAt(0) is { } inner ? ToElement(inner) : null,
        LayoutViewGroup layout => layout.CrossPlatformLayout as Element,
        ContentViewGroup content => content.CrossPlatformLayout as Element,
        _ => null,
    };
}
