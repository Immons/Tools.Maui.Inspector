namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>What is already in a window when a hook lands — the events only cover what comes later.</summary>
internal static class VisualDescendants
{
    public static IEnumerable<Element> Of(IVisualTreeElement root)
    {
        foreach (var child in Children(root))
        {
            if (child is not Element element)
                continue;
            yield return element;
            foreach (var nested in Of(child))
                yield return nested;
        }
    }

    /// <summary>
    /// A window's visual children are its page — the modal stack is not under it. Re-reading a
    /// running app (tracking switched back on) has to include the modals, or whatever is on screen
    /// at that moment is exactly what goes unrecorded.
    /// </summary>
    static IEnumerable<IVisualTreeElement> Children(IVisualTreeElement root)
    {
        if (root is not Window window)
            return root.GetVisualChildren();

        var children = new List<IVisualTreeElement>(((IVisualTreeElement)window).GetVisualChildren());
        if (window.Page is { } page && !children.Contains(page))
            children.Add(page);
        foreach (var modal in window.Navigation?.ModalStack ?? [])
            if (!children.Contains(modal))
                children.Add(modal);
        return children;
    }
}
