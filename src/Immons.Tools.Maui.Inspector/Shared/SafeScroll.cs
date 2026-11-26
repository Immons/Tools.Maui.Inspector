namespace Immons.Tools.Maui.Inspector.Shared;

/// <summary>
/// ScrollTo on an <see cref="ItemsView"/> whose platform view is not loaded yet is deferred by MAUI to
/// the Loaded event. On Windows with MAUI 11 (ItemsViewHandler2 over WinUI's ItemsView) that deferred
/// call throws "ItemsRepeater part is not available" from inside a WinUI event handler, where no catch
/// of ours reaches it and the app dies. So a loaded list scrolls synchronously inside a try, and one
/// that is not loaded yet scrolls a dispatcher turn after Loaded, once its template is in place.
/// </summary>
internal static class SafeScroll
{
    public static void ScrollWhenReady(this ItemsView view, Action<ItemsView> scroll)
    {
        if (view.IsLoaded)
        {
            Try(view, scroll);
            return;
        }

        void OnLoaded(object? sender, EventArgs e)
        {
            view.Loaded -= OnLoaded;
            view.Dispatcher.Dispatch(() => Try(view, scroll));
        }
        view.Loaded += OnLoaded;
    }

    static void Try(ItemsView view, Action<ItemsView> scroll)
    {
        try { scroll(view); }
        catch { /* ScrollTo can throw while the platform list is not built yet or being torn down */ }
    }
}
