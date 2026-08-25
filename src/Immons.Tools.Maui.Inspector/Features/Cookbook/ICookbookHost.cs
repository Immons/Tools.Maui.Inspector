using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Owns the cookbook on the device: the gallery page (at most one, pushed modally, only when
/// asked for), the headless stage the web renders on, and the one focused sample.
/// </summary>
internal interface ICookbookHost
{
    bool IsOpen { get; }

    CookbookPage? Page { get; }

    /// <summary>The gallery page's catalog when open, else a snapshot for headless use.</summary>
    IReadOnlyList<CookbookSection> Catalog { get; }

    /// <summary>Re-reads resources for the headless snapshot (the open page keeps its own).</summary>
    void RebuildCatalog();

    /// <summary>The data context samples get (CookbookOptions.BindingContext), created once.</summary>
    object? SampleContext { get; }

    /// <summary>Off-screen surface for web previews and headless focus. Main thread.</summary>
    CaptureStageHost Stage { get; }

    /// <summary>The focused sample: on its own page when the gallery is open, else headless on the stage.</summary>
    IFocusedSample? Focused { get; }

    /// <summary>True when the focused sample is a page on the device (selectable, inspectable there).</summary>
    bool FocusedOnDevice { get; }

    /// <summary>Opens the page (or focuses it when open), building and scrolling to a section when given. Main thread.</summary>
    Task<bool> OpenAsync(string? sectionId);

    /// <summary>Pops the page when it is the topmost modal. Main thread.</summary>
    Task CloseAsync();

    /// <summary>Focuses the item: a page of its own when the gallery is open on the device, else a full-width headless instance. Main thread.</summary>
    Task<bool> FocusAsync(string itemId);

    /// <summary>Drops the focused sample (pops its page or clears the stage). Main thread.</summary>
    Task UnfocusAsync();

    /// <summary>A resource was edited: samples that copied a resource value repaint from it. Main thread.</summary>
    void RefreshSamples();
}
