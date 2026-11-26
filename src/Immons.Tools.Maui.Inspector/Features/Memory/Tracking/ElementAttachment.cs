namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>Whether an element still belongs to a window — the difference between "in use" and "leaked".</summary>
internal static class ElementAttachment
{
    public static bool IsAttached(Element element) => IsAttached(element, InspectorServices.Current.VisualRoots);

    /// <summary>
    /// Walks Parent up to a live root. A page's tree is live while it hangs under its window; a popped
    /// page or a removed view has no path to one. An overlay whose root only got the window as its
    /// Parent (the page's tree does not list it) is live while its platform view is on a platform
    /// window, a reported root while it is reported, a root found hosted on the platform window but
    /// without any Parent while its platform view is still there.
    /// </summary>
    public static bool IsAttached(Element element, IVisualRoots roots)
    {
        for (var current = element; ; )
        {
            if (current is Window || roots.IsExplicit(current))
                return true;
            switch (current.Parent)
            {
                case null:
                    return roots.IsDiscovered(current) && PlatformRoots.IsOnScreen(current) == true;
                case Window window:
                    // Unknown (no platform to ask) keeps the old answer: under a window is attached.
                    return IsWindowContent(window, current) || PlatformRoots.IsOnScreen(current) != false;
                case var parent:
                    current = parent;
                    break;
            }
        }
    }

    /// <summary>Part of what the window's own tree covers — its page, a modal, a title bar — rather than an overlay root.</summary>
    public static bool InWindowContent(Element element)
    {
        var top = element;
        while (top.Parent is { } parent and not Window)
            top = parent;
        return top.Parent is Window window && IsWindowContent(window, top);
    }

    static bool IsWindowContent(Window window, Element top)
    {
        if (ReferenceEquals(window.Page, top))
            return true;
        if (top is Page page && window.Navigation?.ModalStack is { } modals && modals.Contains(page))
            return true;
        return ((IVisualTreeElement)window).GetVisualChildren().Contains(top);
    }
}
