using System.Collections;
using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// The part of the visual tree a user can actually see and touch: popups and modal pages on top,
/// and inside page containers only the current page (the selected tab, the top of a navigation
/// stack, the presented flyout) — the full tree also holds every page of every tab.
/// </summary>
internal static class LiveTree
{
    /// <summary>Page roots, topmost first: Mopups popups, modal pages (newest first), the window's page.</summary>
    public static List<VisualElement> Roots(IWindowInspector inspector)
    {
        var bottomToTop = inspector.Roots.ToList();
        bottomToTop.AddRange(MopupsStack.Pages());
        bottomToTop.Reverse();
        return bottomToTop;
    }

    /// <summary>The page the user looks at: topmost root, descended through page containers.</summary>
    public static Page? CurrentPage(IWindowInspector inspector) =>
        Roots(inspector).OfType<Page>().Select(Innermost).FirstOrDefault();

    public static Page Innermost(Page page) => page switch
    {
        Shell { CurrentPage: { } current } => Innermost(current),
        NavigationPage { CurrentPage: { } current } => Innermost(current),
        TabbedPage { CurrentPage: { } current } => Innermost(current),
        FlyoutPage { IsPresented: true, Flyout: { } flyout } => Innermost(flyout),
        FlyoutPage { Detail: { } detail } => Innermost(detail),
        _ => page,
    };

    /// <summary>Visible children of an element, page containers reduced to their current page.</summary>
    public static IEnumerable<VisualElement> Children(VisualElement element) => element switch
    {
        Shell { CurrentPage: { } current } => [current],
        NavigationPage { CurrentPage: { } current } => [current],
        TabbedPage { CurrentPage: { } current } => [current],
        FlyoutPage flyout => flyout.IsPresented && flyout.Flyout != null
            ? [flyout.Detail, flyout.Flyout]
            : flyout.Detail != null ? [flyout.Detail] : [],
        _ => VisualTreeWalker.GetVisualChildren(element),
    };
}

/// <summary>
/// Mopups (a popular popup library) shows its pages outside the navigation stacks, in a window
/// of their own on iOS. Read through reflection so the inspector does not depend on it.
/// </summary>
internal static class MopupsStack
{
    static bool _resolved;
    static PropertyInfo? _instance;
    static PropertyInfo? _stack;
    static MethodInfo? _pop;

    public static IEnumerable<VisualElement> Pages()
    {
        if (Service() is not { } service || _stack?.GetValue(service) is not IEnumerable pages)
            return [];
        return pages.OfType<VisualElement>().ToList();
    }

    /// <summary>Pops the topmost popup; false when there is none (or Mopups is not in the app).</summary>
    public static async Task<bool> PopAsync()
    {
        if (!Pages().Any() || Service() is not { } service || _pop == null)
            return false;
        var arguments = _pop.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : (object?)true).ToArray();
        if (_pop.Invoke(service, arguments) is Task task)
            await task.ConfigureAwait(true);
        return true;
    }

    static object? Service()
    {
        if (!_resolved)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Mopups");
                if (assembly == null)
                    return null; // not loaded (yet) — look again next time
                _resolved = true;
                var type = assembly.GetType("Mopups.Services.MopupService");
                _instance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var serviceType = _instance?.PropertyType;
                _stack = serviceType?.GetProperty("PopupStack");
                _pop = serviceType?.GetMethods().FirstOrDefault(m => m.Name == "PopAsync" && m.GetParameters().Length <= 1);
            }
            catch
            {
                _instance = null;
            }
        }

        try
        {
            return _instance?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }
}
