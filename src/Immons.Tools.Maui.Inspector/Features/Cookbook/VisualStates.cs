namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Names of the visual states a sample declares — its own or via its style.</summary>
internal static class VisualStates
{
    public static IReadOnlyList<string> NamesOf(VisualElement? sample)
    {
        var names = new List<string>();
        if (sample == null)
            return names;
        try
        {
            foreach (var group in GroupsOf(sample))
            {
                foreach (var state in group.States)
                {
                    if (!string.IsNullOrEmpty(state.Name))
                        names.Add(state.Name);
                }
            }
        }
        catch
        {
            // no groups — nothing to force
        }
        return names;
    }

    /// <summary>
    /// Read through the attached property, not <c>VisualStateManager.GetVisualStateGroups</c>: its
    /// return type changed from <c>IList&lt;VisualStateGroup&gt;</c> (MAUI 10) to
    /// <c>VisualStateGroupList</c> (MAUI 11), so a call compiled against 10 is a missing method on 11
    /// (MissingMethodException on CoreCLR/Mono, "will always throw" under NativeAOT). The property
    /// and the list type are the same in both.
    /// </summary>
    internal static IEnumerable<VisualStateGroup> GroupsOf(VisualElement sample) =>
        sample.GetValue(VisualStateManager.VisualStateGroupsProperty) as IEnumerable<VisualStateGroup> ?? [];
}
