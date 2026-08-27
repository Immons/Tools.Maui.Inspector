using System.Reflection;
using System.Runtime.CompilerServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

/// <summary>
/// Pages entering and leaving a window. A pop in watch mode schedules one snapshot after the
/// configured delay — several pops in a row share it — and the bisection aids run on the popped
/// page right away. Every snapshot, manual or scheduled, judges the pending pages.
/// </summary>
internal sealed class NavigationWatcher(INavigationLedger ledger, ISnapshotRunner snapshots, ITrackedInstances instances) : INavigationWatcher
{
    static readonly Assembly Self = typeof(NavigationWatcher).Assembly;

    readonly ConditionalWeakTable<Window, object> _hooked = new();
    CancellationTokenSource? _scheduled;
    bool _judging;

    public void Attach(Window window)
    {
        if (_hooked.TryGetValue(window, out _))
            return;
        _hooked.Add(window, new object());
        if (!_judging)
        {
            _judging = true;
            snapshots.Taken += _ => ledger.Judge();
        }

        foreach (var existing in VisualDescendants.Of(window).OfType<Page>())
        {
            if (existing.GetType().Assembly != Self)
                ledger.Pushed(existing);
        }
        window.DescendantAdded += (_, e) =>
        {
            if (e.Element is Page page && page.GetType().Assembly != Self)
                OnPushed(page);
        };
        window.DescendantRemoved += (_, e) =>
        {
            if (e.Element is Page page && page.GetType().Assembly != Self)
                OnPopped(page);
        };
    }

    /// <summary>
    /// A screen the app itself puts on screen — an overlay host's layer, a custom modal, anything
    /// that never becomes a Page in a Window and so cannot be seen in the visual tree. Reported
    /// objects join the ledger and the tracker, and get the same verdicts as pushed pages.
    /// </summary>
    public void ReportPushed(object screen, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ledger.Pushed(screen, name, reported: true);
        instances.Track(screen, screen is Element ? TrackedKind.Element : TrackedKind.BindingContext, name);
        Reconsider();
    }

    /// <summary>
    /// A screen appearing matters as much as one leaving: what looked detached a moment ago —
    /// state the app parks between screens — is in use again the moment the next screen binds it.
    /// Without this the list would keep accusing it until someone pressed the button.
    /// </summary>
    void OnPushed(Page page)
    {
        ledger.Pushed(page);
        Reconsider();
    }

    /// <summary>Watch mode only: one snapshot once navigation settles, shared by everything that moved.</summary>
    void Reconsider()
    {
        var options = MauiInspector.Options.Memory;
        if (options.WatchNavigation)
            Schedule(options.WatchDelay);
    }

    public void ReportPopped(object screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        OnPopped(screen);
    }

    void OnPopped(object screen)
    {
        ledger.Popped(screen);
        var options = MauiInspector.Options.Memory;
        if (screen is VisualElement element)
        {
            if (options.DisconnectHandlersOnPop)
                element.DisconnectHandlers();
            if (options.ClearBindingContextOnPop)
                element.BindingContext = null;
        }
        Reconsider();
    }

    void Schedule(TimeSpan delay)
    {
        _scheduled?.Cancel();
        var cts = new CancellationTokenSource();
        _scheduled = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                await snapshots.RunAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // superseded by a later pop
            }
            catch (Exception ex)
            {
                InspectorServices.Current.Logs.Write(Microsoft.Extensions.Logging.LogLevel.Warning, "MauiInspector", "watch-mode snapshot failed: " + ex.Message);
            }
        });
    }
}
