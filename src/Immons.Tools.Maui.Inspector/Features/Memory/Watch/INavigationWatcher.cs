namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

/// <summary>Feeds the ledger from a window's pages and from what the app reports; in watch mode, snapshots after a pop.</summary>
internal interface INavigationWatcher : IInspectorNavigation
{
    void Attach(Window window);
}
