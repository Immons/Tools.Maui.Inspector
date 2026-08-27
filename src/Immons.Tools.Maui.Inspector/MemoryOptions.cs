namespace Immons.Tools.Maui.Inspector;

/// <summary>Tuning of the Memory view — instance tracking, leak snapshots, navigation watching and heap dumps.</summary>
public sealed class MemoryOptions
{
    /// <summary>
    /// Keeps a weak reference to every element that enters a window, plus its view model, handler
    /// and platform view, so a snapshot can tell which of them outlived their page. Costs one weak
    /// reference per object and nothing else. Default: true.
    /// </summary>
    public bool TrackInstances { get; set; } = true;

    /// <summary>
    /// Full GC rounds (collect + wait for finalizers) a snapshot runs before deciding what is still
    /// alive. MAUI needs several: handlers and platform peers are released a round late. Default: 5.
    /// </summary>
    public int CollectionsPerSnapshot { get; set; } = 5;

    /// <summary>
    /// Watch mode: after every page that leaves a window, a snapshot runs by itself (debounced by
    /// <see cref="WatchDelay"/>) and the navigation ledger records whether the page was collected.
    /// Each snapshot is a few full collections, so this is off by default; the panel toggles it too.
    /// </summary>
    public bool WatchNavigation { get; set; }

    /// <summary>How long after the last page pop the watch-mode snapshot waits — animations and handlers need a moment. Default: 2 s.</summary>
    public TimeSpan WatchDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Bisection aid: calls DisconnectHandlers() on every page that leaves a window. If the leak
    /// suspects vanish with this on, the handlers were what kept the page. Debug builds only — it is a diagnosis, not a fix.
    /// </summary>
    public bool DisconnectHandlersOnPop { get; set; }

    /// <summary>Bisection aid: clears the BindingContext of every page that leaves a window (see <see cref="DisconnectHandlersOnPop"/>).</summary>
    public bool ClearBindingContextOnPop { get; set; }

    /// <summary>
    /// Called after a snapshot that finds objects of the app's own types still alive with no window
    /// using them — once per new set of suspects. The hook for UI tests: fail the run when it fires.
    /// </summary>
    public Action<IReadOnlyList<LeakedObject>>? OnLeak { get; set; }

    /// <summary>
    /// What counts as "the app's own code" in the Memory view, as assembly-name prefixes. Empty
    /// (default) means: the assembly the App class lives in and everything sharing its root name
    /// ("Contoso.Shop.Mobile" also owns "Contoso.Shop.Model"). Everything else that is not the
    /// framework — CommunityToolkit, SQLite-net, Mapster — is shown as a package, not as yours.
    /// </summary>
    public IList<string> AppAssemblyPrefixes { get; } = new List<string>();
}
