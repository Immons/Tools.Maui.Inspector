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
            foreach (var group in VisualStateManager.GetVisualStateGroups(sample))
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
}
