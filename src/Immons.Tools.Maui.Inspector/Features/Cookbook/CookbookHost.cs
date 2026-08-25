using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Pushes the cookbook page modally when asked, keeps the headless stage for the web panel, and
/// tracks the one focused sample — on a page above the gallery, or on the stage.
/// </summary>
internal sealed class CookbookHost(ICookbookCatalog catalog) : ICookbookHost
{
    bool _hooked;
    bool _contextCreated;
    object? _context;
    IReadOnlyList<CookbookSection>? _snapshot;
    CookbookSamplePage? _samplePage;
    StagedSample? _staged;

    public CookbookPage? Page { get; private set; }

    public bool IsOpen => Page != null;

    public IReadOnlyList<CookbookSection> Catalog => Page?.Catalog ?? (_snapshot ??= catalog.Build());

    public void RebuildCatalog()
    {
        if (Page == null)
            _snapshot = catalog.Build();
    }

    public object? SampleContext
    {
        get
        {
            if (!_contextCreated)
            {
                _contextCreated = true;
                try
                {
                    _context = MauiInspector.Options.Cookbook.BindingContext?.Invoke();
                }
                catch
                {
                    _context = null; // a failing factory must not keep the gallery from opening
                }
            }
            return _context;
        }
    }

    public CaptureStageHost Stage { get; } = new();

    public IFocusedSample? Focused => (IFocusedSample?)_samplePage ?? _staged;

    public bool FocusedOnDevice => _samplePage != null;

    public async Task<bool> OpenAsync(string? sectionId)
    {
        if (Page is { } open)
        {
            if (sectionId != null)
                open.ShowSection(sectionId);
            return true;
        }

        if (RootPage() is not { } root)
            return false;

        HookModalStack();
        ClearStagedFocus(); // the page takes over; a headless focus would be a stale twin
        var page = new CookbookPage(catalog.Build(), MauiInspector.Options.Cookbook, SampleContext);
        page.CloseRequested += () => _ = CloseAsync();
        page.FocusRequested += item => _ = FocusAsync(item.Id);
        Page = page;
        try
        {
            await root.Navigation.PushModalAsync(page, animated: true);
        }
        catch
        {
            Page = null;
            return false;
        }

        if (sectionId != null)
            page.ShowSection(sectionId);
        return true;
    }

    public async Task CloseAsync()
    {
        if (Page is not { } page)
            return;
        await UnfocusAsync();
        Page = null;

        await PopIfTop(page);
    }

    static async Task PopIfTop(Page page)
    {
        var root = RootPage();
        IReadOnlyList<Page>? modals = null;
        try { modals = root?.Navigation?.ModalStack; }
        catch { /* navigation may be unavailable mid-teardown */ }
        if (root == null || modals == null || modals.Count == 0 || !ReferenceEquals(modals[^1], page))
            return; // something else sits above it — leave the stack alone

        try
        {
            await root.Navigation.PopModalAsync();
        }
        catch
        {
            // already being dismissed
        }
    }

    public async Task<bool> FocusAsync(string itemId)
    {
        if (Focused is { } current)
        {
            if (current.Item.Id == itemId)
                return true;
            await UnfocusAsync();
        }
        if (Catalog.SelectMany(s => s.Items).FirstOrDefault(i => i.Id == itemId) is not { } item)
            return false;

        if (Page is { } page)
            return await FocusOnDevice(page, item);

        // Headless: the control alone at the window's width, off screen — the device shows the app.
        if (!Stage.EnsureAttached(SampleContext))
            return false;
        var width = Math.Max(240, Stage.Width - 2 * CookbookGridMetrics.Spacing);
        var staged = new StagedSample(item, width, SampleContext);
        CookbookBackdrop.Paint(staged.Host, MauiInspector.Options.Cookbook);
        Stage.Add(staged.Host);
        _staged = staged;
        return true;
    }

    async Task<bool> FocusOnDevice(CookbookPage page, CookbookItem item)
    {
        if (RootPage() is not { } root)
            return false;
        var sample = new CookbookSamplePage(item, SampleContext, page);
        sample.CloseRequested += () => _ = UnfocusAsync();
        _samplePage = sample;
        try
        {
            await root.Navigation.PushModalAsync(sample, animated: true);
            return true;
        }
        catch
        {
            _samplePage = null;
            return false;
        }
    }

    public async Task UnfocusAsync()
    {
        ClearStagedFocus();
        if (_samplePage is not { } sample)
            return;
        _samplePage = null;
        await PopIfTop(sample);
    }

    void ClearStagedFocus()
    {
        if (_staged is not { } staged)
            return;
        _staged = null;
        Stage.Remove(staged.Host);
    }

    public void RefreshSamples() => Page?.RefreshSamples();

    /// <summary>The page may be dismissed by the app itself (a swipe on iOS) — forget it then.</summary>
    void HookModalStack()
    {
        if (_hooked || Application.Current is not { } app)
            return;
        _hooked = true;
        app.ModalPopped += (_, e) =>
        {
            if (ReferenceEquals(e.Modal, Page))
                Page = null;
            if (ReferenceEquals(e.Modal, _samplePage))
                _samplePage = null;
        };
    }

    static Page? RootPage() =>
        Application.Current?.Windows.FirstOrDefault(w => w.Handler != null)?.Page
        ?? Application.Current?.Windows.FirstOrDefault()?.Page;
}
