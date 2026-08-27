using System.Reflection;
using System.Runtime.CompilerServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>
/// Hooks a window's DescendantAdded/Removed — MAUI raises them for every element that enters or
/// leaves the tree at any depth, pages included — and records each element with its view model,
/// handler and platform view. Only weak references are kept; the per-element event subscriptions
/// die with the element, so tracking never keeps anything alive.
/// </summary>
internal sealed class InstanceTracker(ITrackedInstances instances) : IInstanceTracker
{
    static readonly Assembly Self = typeof(InstanceTracker).Assembly;

    readonly ConditionalWeakTable<Window, object> _hooked = new();
    // Weakly, so a window that closes is not kept alive by the ability to re-read it later.
    readonly List<WeakReference<Window>> _windows = [];

    public bool Enabled => MauiInspector.Options.Memory.TrackInstances;

    /// <summary>
    /// Hooked once and for good — the events are two subscriptions per window and cost nothing
    /// while tracking is off, whereas re-hooking on every toggle would miss whatever happened in
    /// between and risk double subscriptions.
    /// </summary>
    public void Attach(Window window)
    {
        if (_hooked.TryGetValue(window, out _))
            return;
        _hooked.Add(window, new object());
        lock (_windows)
        {
            _windows.Add(new WeakReference<Window>(window));
        }

        window.DescendantAdded += (_, e) => Register(e.Element);
        window.DescendantRemoved += (_, e) =>
        {
            if (Enabled)
                instances.MarkDetached(e.Element, DateTime.Now);
        };
        if (Enabled)
            ReadTree(window);
    }

    public void SetEnabled(bool enabled)
    {
        var options = MauiInspector.Options.Memory;
        if (options.TrackInstances == enabled)
            return;
        options.TrackInstances = enabled;

        if (!enabled)
        {
            // Nothing recorded, nothing remembered: with the registry empty the inspector holds no
            // reference to the app's objects at all.
            options.WatchNavigation = false;
            instances.Clear();
            return;
        }

        var windows = LiveWindows();
        foreach (var window in windows)
            ReadTree(window);
        InspectorServices.Current.Logs.Write(Microsoft.Extensions.Logging.LogLevel.Debug, "MauiInspector",
            $"tracking on — re-read {windows.Count()} window(s), {instances.Count} objects");
    }

    void ReadTree(Window window)
    {
        foreach (var element in VisualDescendants.Of(window))
            Register(element);
    }

    IEnumerable<Window> LiveWindows()
    {
        lock (_windows)
        {
            _windows.RemoveAll(w => !w.TryGetTarget(out _));
            return _windows.Select(w => w.TryGetTarget(out var window) ? window : null).OfType<Window>().ToList();
        }
    }

    void Register(Element element)
    {
        // The inspector's own overlay is not the app's memory.
        if (!Enabled || element.GetType().Assembly == Self)
            return;

        instances.MarkAttached(element);
        if (!instances.TryGet(element, out _))
        {
            instances.Track(element, TrackedKind.Element, null);
            // -= first: after tracking is switched off and on again the element is new to the
            // registry but not to us, and a second handler would run everything twice.
            element.BindingContextChanged -= OnBindingContextChanged;
            element.BindingContextChanged += OnBindingContextChanged;
            element.HandlerChanged -= OnHandlerChanged;
            element.HandlerChanged += OnHandlerChanged;
        }
        TrackContext(element);
        TrackHandler(element);
    }

    void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (Enabled && sender is Element element)
            TrackContext(element);
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (Enabled && sender is Element element)
            TrackHandler(element);
    }

    void TrackContext(Element element)
    {
        if (element.BindingContext is { } context && IsViewModelLike(context))
            instances.Track(context, TrackedKind.BindingContext, TypeNames.Short(element.GetType()));
    }

    void TrackHandler(Element element)
    {
        if (element.Handler is not { } handler)
            return;
        var owner = TypeNames.Short(element.GetType());
        instances.Track(handler, TrackedKind.Handler, owner);
        if (handler.PlatformView is { } platformView)
            instances.Track(platformView, TrackedKind.PlatformView, owner);
    }

    /// <summary>Strings and value types are not worth a row; an element as its own context is already tracked.</summary>
    static bool IsViewModelLike(object context) =>
        context is not (string or Element or Delegate) && context.GetType().IsClass;
}
