using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>The MAUI-specific reading of a detached object — what usually explains why it is still here.</summary>
internal static class SuspectHints
{
    public static IReadOnlyList<string> For(TrackedKind kind, object target)
    {
        var hints = new List<string>();
        switch (kind)
        {
            case TrackedKind.Element when target is Element element:
                if (element is Page)
                    hints.Add("page");
                if (element.Handler != null)
                    hints.Add("handler still connected — DisconnectHandler never ran");
                // A child of a detached page is only along for the ride: name the tree it hangs in.
                if (Top(element) is { } top && top != element)
                    hints.Add($"inside {TypeNames.Short(top.GetType())}");
                if (element.BindingContext is { } context && context is not string)
                    hints.Add($"BindingContext: {TypeNames.Short(context.GetType())}");
                break;
            case TrackedKind.Handler when target is IElementHandler handler:
                if (handler.VirtualView is { } view)
                    hints.Add($"view: {TypeNames.Short(view.GetType())}");
                if (handler.PlatformView != null)
                    hints.Add("platform view still alive");
                break;
            case TrackedKind.BindingContext:
                hints.Add("no attached element is bound to it");
                break;
        }
        return hints;
    }

    /// <summary>The outermost element of a detached subtree — the page that leaked, usually.</summary>
    static Element? Top(Element element)
    {
        var top = element;
        while (top.Parent is { } parent)
            top = parent;
        return top;
    }
}
