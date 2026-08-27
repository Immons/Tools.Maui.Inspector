namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>Whether an element still belongs to a window — the difference between "in use" and "leaked".</summary>
internal static class ElementAttachment
{
    /// <summary>Walks Parent up to a Window: a popped page or a removed view has no path to one.</summary>
    public static bool IsAttached(Element element)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            if (current is Window)
                return true;
        }
        return false;
    }
}
