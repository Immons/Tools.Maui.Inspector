using System.Runtime.CompilerServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>
/// Visual roots the window's tree events never reach: an overlay root that only got the window as
/// its Parent, or none at all, raises no DescendantAdded on the window. Reported roots are read when
/// reported; roots hosted on the platform window are found by a light scan of its direct children
/// every <see cref="ScanInterval"/> (while tracking is on or the on-device panel is open), before every
/// snapshot and whenever the inspector reads its element tree. A root's own descendant events keep
/// its tree up to date while it is known; when it goes away its elements are marked detached.
/// </summary>
internal sealed partial class InstanceTracker
{
    static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1.5);

    readonly ConditionalWeakTable<Element, object> _hookedRoots = new();
    IDispatcherTimer? _scan;

    public void TrackRoot(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!roots.AddExplicit(root))
            return;
        OnMain(() => ShowRoot(root));
    }

    public void UntrackRoot(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!roots.RemoveExplicit(root))
            return;
        OnMain(() => HideRoot(root));
    }

    /// <summary>
    /// The scan runs whenever asked — the element tree lists the roots with tracking off too — and
    /// only the registration of their elements depends on tracking.
    /// </summary>
    public void RefreshRoots()
    {
        if (!MauiInspector.Options.Memory.DiscoverVisualRoots)
            return;
        var found = new List<Element>();
        foreach (var window in LiveWindows())
        {
            foreach (var root in PlatformRoots.Hosted(window))
            {
                if (!IsOwn(root) && !ElementAttachment.InWindowContent(root) && !found.Contains(root))
                    found.Add(root);
            }
        }

        var (appeared, vanished) = roots.Scanned(found);
        foreach (var root in appeared)
            ShowRoot(root);
        foreach (var root in vanished)
            HideRoot(root);
    }

    void StartRootScan(Window window)
    {
        if (_scan != null || window.Dispatcher is not { } dispatcher)
            return;
        _scan = dispatcher.CreateTimer();
        _scan.Interval = ScanInterval;
        _scan.IsRepeating = true;
        _scan.Tick += (_, _) =>
        {
            if (Enabled || MauiInspector.ActiveInspector?.IsShown == true)
                RefreshRoots();
        };
        _scan.Start();
    }

    void ShowRoot(Element root)
    {
        if (!Enabled || IsOwn(root))
            return;
        HookRoot(root);
        Register(root);
        foreach (var element in VisualDescendants.Of(root))
            Register(element);
    }

    /// <summary>Only what no other live root still holds — a reported layer can sit in a page too.</summary>
    void HideRoot(Element root)
    {
        if (!Enabled)
            return;
        var now = DateTime.Now;
        foreach (var element in VisualDescendants.Of(root).Prepend(root))
        {
            if (!ElementAttachment.IsAttached(element, roots))
                instances.MarkDetached(element, now);
        }
    }

    /// <summary>
    /// A root under the window raises its descendants' events on the window too; one without a
    /// Parent raises them only on itself. Hooked once — the handlers die with the root.
    /// </summary>
    void HookRoot(Element root)
    {
        if (_hookedRoots.TryGetValue(root, out _))
            return;
        _hookedRoots.Add(root, new object());
        root.DescendantAdded += (_, e) => Register(e.Element);
        root.DescendantRemoved += (_, e) =>
        {
            if (Enabled)
                instances.MarkDetached(e.Element, DateTime.Now);
        };
    }

    static bool IsOwn(Element element) => element.GetType().Assembly == Self;

    static void OnMain(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }
}
