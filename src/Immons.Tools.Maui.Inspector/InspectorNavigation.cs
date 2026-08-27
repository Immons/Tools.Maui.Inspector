namespace Immons.Tools.Maui.Inspector;

/// <summary>
/// Screens the inspector cannot see on its own, reported so the Memory view's navigation ledger
/// can judge them like any pushed page: an overlay layer added to the current page, a custom modal,
/// a tab host of your own — anything that never becomes a <see cref="Page"/> in a <see cref="Window"/>.
/// </summary>
/// <remarks>
/// The name deliberately collides with <c>VisualElement.Navigation</c>: inside a page, the bare
/// <c>Navigation</c> is still MAUI's, and this one is reached as
/// <c>Immons.Tools.Maui.Inspector.Navigation</c> — the inspector never quietly takes over a name
/// the app already uses.
/// </remarks>
/// <example>
/// <code>
/// // in the overlay host
/// Immons.Tools.Maui.Inspector.Navigation.ReportPushed(layer, "CheckoutOverlay");
/// …
/// Immons.Tools.Maui.Inspector.Navigation.ReportPopped(layer);
/// </code>
/// </example>
public static class Navigation
{
    /// <summary>
    /// A screen appeared. Reported screens join the navigation ledger and get the same verdict
    /// after the next snapshot — collected, or still alive. Only a weak reference is kept, so
    /// reporting never keeps anything alive.
    /// </summary>
    /// <param name="screen">The view or the view model — whatever should not outlive the screen.</param>
    /// <param name="name">What to call it in the ledger; its own name or type by default.</param>
    public static void ReportPushed(object screen, string? name = null) =>
        Inspector.InspectorServices.Current.Navigation.ReportPushed(screen, name);

    /// <summary>
    /// The reported screen went away. The ledger then waits for a snapshot to decide whether it was
    /// collected — with watch mode on, that snapshot is taken by itself.
    /// </summary>
    public static void ReportPopped(object screen) =>
        Inspector.InspectorServices.Current.Navigation.ReportPopped(screen);
}

/// <summary>The seam behind <see cref="Navigation"/>.</summary>
internal interface IInspectorNavigation
{
    void ReportPushed(object screen, string? name = null);

    void ReportPopped(object screen);
}
