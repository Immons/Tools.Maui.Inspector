using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// The headless render surface: a MAUI layout hosted as a platform view of the app's window but
/// kept off screen, and logically parented to the page currently presented — so implicit styles,
/// resources, DynamicResource and the theme apply exactly as on that page. Web previews and
/// focused samples render here while the device keeps showing the app; the cookbook page only
/// appears when opened on purpose.
/// </summary>
internal sealed partial class CaptureStageHost
{
    readonly CaptureStage _stage = new();
    Window? _window;

    /// <summary>The window's width in dp (0 until attached).</summary>
    public double Width => _window?.Width > 0 ? _window.Width : 0;

    /// <summary>Attaches on first use and follows the presented page for resources. Main thread.</summary>
    public bool EnsureAttached(object? sampleContext)
    {
        var window = Application.Current?.Windows.FirstOrDefault(w => w.Handler != null);
        if (window?.Handler?.MauiContext is not { } mauiContext)
            return false;

        if (_window == null)
        {
            _stage.BindingContext = sampleContext; // explicit — the page's view model must not leak into the samples
            if (!AttachPlatform(window, mauiContext))
                return false;
            _window = window;
        }

        var page = PresentedPage(window);
        if (page != null && !ReferenceEquals(_stage.Parent, page))
            _stage.Parent = page; // resources, implicit styles and DynamicResource resolve through the page
        return true;
    }

    public void Add(View view) => _stage.Add(view);

    public void Remove(View view) => _stage.Remove(view);

    /// <summary>The page the user sees: the top modal, else the deepest presented page of the root.</summary>
    static Page? PresentedPage(Window window)
    {
        var page = window.Page;
        try
        {
            if (page?.Navigation?.ModalStack is { Count: > 0 } modals)
                page = modals[^1];
        }
        catch
        {
            // navigation may be unavailable mid-teardown
        }
        while (true)
        {
            Page? inner = page switch
            {
                Shell shell => shell.CurrentPage,
                NavigationPage navigation => navigation.CurrentPage,
                TabbedPage tabbed => tabbed.CurrentPage,
                FlyoutPage flyout => flyout.Detail,
                _ => null,
            };
            if (inner == null || ReferenceEquals(inner, page))
                return page;
            page = inner;
        }
    }

    private partial bool AttachPlatform(Window window, IMauiContext context);
}
