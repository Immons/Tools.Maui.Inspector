using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>
/// The single walker behind the Resources popup and the cookbook: application dictionary,
/// merged dictionaries (recursively) and the presented pages' own dictionaries, each paired
/// with the file the sync tool should patch — the dictionary's Source, or for inline page
/// resources the page's own source file.
/// </summary>
internal sealed class ResourceScopes(IActiveInspectorProvider inspectors) : IResourceScopes
{
    // A dictionary loaded via Source= keeps its content — and its own nested merges — in a
    // private instance; Keys/TryGetValue see the content, MergedDictionaries does not see the merges.
    static readonly FieldInfo? MergedInstanceField =
        typeof(ResourceDictionary).GetField("_mergedInstance", BindingFlags.NonPublic | BindingFlags.Instance);

    public IReadOnlyList<ResourceScope> All()
    {
        var scopes = new List<ResourceScope>();
        var seen = new HashSet<ResourceDictionary>();

        if (Application.Current?.Resources is { } appResources)
        {
            seen.Add(appResources);
            if (appResources.Keys.Any())
                scopes.Add(new ResourceScope("Application", appResources, appResources.Source?.OriginalString));
            AddMerged(appResources, scopes, seen);
        }

        foreach (var page in PresentedPages())
        {
            if (!seen.Add(page.Resources) || !page.Resources.Keys.Any())
                continue;
            string? pageSource = null;
            try { pageSource = Microsoft.Maui.VisualDiagnostics.GetSourceInfo(page)?.SourceUri?.ToString(); }
            catch { /* diagnostics may be off */ }
            scopes.Add(new ResourceScope($"Page · {page.GetType().Name}", page.Resources,
                page.Resources.Source?.OriginalString ?? pageSource));
        }

        return scopes;
    }

    static void AddMerged(ResourceDictionary parent, List<ResourceScope> scopes, HashSet<ResourceDictionary> seen)
    {
        foreach (var merged in MergedOf(parent))
        {
            if (!seen.Add(merged))
                continue;
            scopes.Add(new ResourceScope(MergedName(merged), merged, merged.Source?.OriginalString));
            AddMerged(merged, scopes, seen);
        }
    }

    static IEnumerable<ResourceDictionary> MergedOf(ResourceDictionary dictionary)
    {
        foreach (var merged in dictionary.MergedDictionaries)
            yield return merged;

        ResourceDictionary? inner = null;
        try { inner = MergedInstanceField?.GetValue(dictionary) as ResourceDictionary; }
        catch { /* internal layout changed — nested merges of Source-loaded dictionaries stay unlisted */ }
        if (inner == null)
            yield break;
        foreach (var merged in inner.MergedDictionaries)
            yield return merged;
    }

    /// <summary>Every page that may carry resources right now: the root, whatever it currently
    /// presents (Shell/NavigationPage/tabs), and the modal stack.</summary>
    IEnumerable<Page> PresentedPages()
    {
        var seen = new HashSet<Page>();

        void Add(Page? page)
        {
            if (page == null || !seen.Add(page))
                return;
            switch (page)
            {
                case Shell shell:
                    Add(shell.CurrentPage);
                    break;
                case NavigationPage navigation:
                    Add(navigation.CurrentPage);
                    break;
                case FlyoutPage flyout:
                    Add(flyout.Detail);
                    break;
                case TabbedPage tabbed:
                    Add(tabbed.CurrentPage);
                    break;
            }
        }

        foreach (var root in inspectors.Current?.Roots ?? [])
        {
            if (root is not Page page)
                continue;
            Add(page);
            IReadOnlyList<Page>? modals = null;
            try { modals = page.Navigation?.ModalStack; }
            catch { /* navigation may be unavailable mid-teardown */ }
            foreach (var modal in modals ?? [])
                Add(modal);
        }

        return seen;
    }

    static string MergedName(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        if (string.IsNullOrEmpty(source))
            return dictionary.GetType() != typeof(ResourceDictionary) ? dictionary.GetType().Name : "Merged";
        return source.Split(';')[0].TrimStart('/');
    }
}
