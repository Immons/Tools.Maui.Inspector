using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>Where the app is (current page, stacks, tabs) and the back action. Main thread.</summary>
internal static class RemoteNavigation
{
    public static JsonObject Describe(IWindowInspector inspector, ElementLocator locator)
    {
        var main = inspector.Roots.FirstOrDefault() as Page;
        var current = LiveTree.CurrentPage(inspector);

        var json = new JsonObject
        {
            ["page"] = current != null ? PageJson(current) : null,
            ["root"] = main?.GetType().Name,
        };

        if (main is Shell shell)
            json["route"] = shell.CurrentState?.Location?.ToString();

        var navigation = new JsonArray();
        try
        {
            foreach (var page in current?.Navigation?.NavigationStack ?? [])
            {
                if (page != null)
                    navigation.Add(PageJson(page));
            }
        }
        catch
        {
            // navigation unavailable mid-transition
        }
        json["navigationStack"] = navigation;

        var modals = new JsonArray();
        try
        {
            foreach (var page in main?.Navigation?.ModalStack ?? [])
                modals.Add(PageJson(LiveTree.Innermost(page)));
        }
        catch
        {
            // navigation unavailable mid-transition
        }
        json["modalStack"] = modals;

        var popups = new JsonArray();
        foreach (var popup in MopupsStack.Pages())
            popups.Add(new JsonObject { ["type"] = popup.GetType().Name, ["automationId"] = NullIfEmpty(popup.AutomationId) });
        json["popups"] = popups;

        if (current != null && Ancestor<TabbedPage>(current) is { } tabs)
        {
            var tabArray = new JsonArray();
            foreach (var tab in tabs.Children)
            {
                tabArray.Add(new JsonObject
                {
                    ["title"] = tab.Title,
                    ["selected"] = ReferenceEquals(tab, tabs.CurrentPage),
                });
            }
            json["tabs"] = tabArray;
        }

        json["window"] = new JsonObject
        {
            ["width"] = inspector.WindowSize.Width,
            ["height"] = inspector.WindowSize.Height,
            ["scale"] = NativeUi.Scale(),
        };

        if (locator.FocusedInput() is { } focused)
        {
            json["focused"] = new JsonObject
            {
                ["type"] = focused.GetType().Name,
                ["automationId"] = NullIfEmpty(focused.AutomationId),
                ["placeholder"] = focused.Placeholder,
            };
        }

        return json;
    }

    /// <summary>
    /// Back the way the platform button does it: the topmost popup, then the modal page's own
    /// back handling or the modal itself, then the page's (a NavigationPage pops, Shell goes up a
    /// route). The pages' OnBackButtonPressed overrides get their say, as on Android.
    /// </summary>
    public static async Task<string> BackAsync(IWindowInspector inspector)
    {
        if (await MopupsStack.PopAsync())
            return "popup";

        if (inspector.Roots.FirstOrDefault() is not Page main)
            return "none";

        var modals = main.Navigation?.ModalStack ?? [];
        if (modals.Count > 0)
        {
            if (modals[^1].SendBackButtonPressed())
                return "modal-navigation";
            await main.Navigation!.PopModalAsync();
            return "modal";
        }

        if (main is Shell shell)
        {
            if (shell.Navigation.NavigationStack.Count > 1)
            {
                await shell.GoToAsync("..");
                return "shell";
            }
            return "none";
        }

        return main.SendBackButtonPressed() ? "navigation" : "none";
    }

    static JsonObject PageJson(Page page) => new()
    {
        ["type"] = page.GetType().Name,
        ["title"] = NullIfEmpty(page.Title),
        ["automationId"] = NullIfEmpty(page.AutomationId),
    };

    static T? Ancestor<T>(Element element) where T : Element
    {
        for (var current = element.Parent; current != null; current = current.Parent)
        {
            if (current is T match)
                return match;
        }
        return null;
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
