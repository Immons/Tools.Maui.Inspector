using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>
/// The way up the logical tree from a detached element: what it sits in, up to the oldest
/// ancestor — the subtree that was dropped as a whole, whose top is what the heap dump has to
/// explain. Labels carry the x:Name / AutomationId when there is one.
/// </summary>
internal static class ParentChain
{
    const int MaxDepth = 60;

    public static IReadOnlyList<string> Of(Element element, bool includeSelf)
    {
        var chain = new List<string>();
        var current = includeSelf ? element : element.Parent;
        for (var depth = 0; current != null && depth < MaxDepth; depth++, current = current.Parent)
            chain.Add(Label(current));
        return chain;
    }

    public static string Label(Element element)
    {
        var label = TypeNames.Short(element.GetType());
        if (!string.IsNullOrEmpty(element.StyleId))
            return label + " @" + element.StyleId;
        return string.IsNullOrEmpty(element.AutomationId) ? label : label + " #" + element.AutomationId;
    }
}
