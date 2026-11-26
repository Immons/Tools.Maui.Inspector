namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>What the platform window itself hosts — MAUI views added next to the page's own view.</summary>
internal static partial class PlatformRoots
{
    /// <summary>MAUI elements whose platform views are direct children of the window's platform root (main thread).</summary>
    public static List<Element> Hosted(Window window)
    {
        var roots = new List<Element>();
        CollectHosted(window, roots);
        return roots;
    }

    /// <summary>Whether the element's platform view is in a platform window; null where the platform cannot tell.</summary>
    public static bool? IsOnScreen(Element element)
    {
        bool? onScreen = null;
        ReadOnScreen(element, ref onScreen);
        return onScreen;
    }

    static partial void CollectHosted(Window window, List<Element> roots);

    static partial void ReadOnScreen(Element element, ref bool? onScreen);
}
