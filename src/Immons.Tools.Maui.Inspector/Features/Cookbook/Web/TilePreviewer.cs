using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

/// <summary>A tile rendered to PNG plus the visual states its sample declares.</summary>
internal sealed record TilePreview(byte[]? Png, IReadOnlyList<string> States, string? Error);

/// <summary>
/// Renders one item: the focused instance when it is this item (forced visual states included),
/// else the tile on the device screen when the gallery shows it, else a throw-away tile on the
/// headless stage — the device never changes for a preview.
/// </summary>
internal sealed class TilePreviewer(IMainThreadDispatcher mainThread, ICookbookHost host)
{
    const int Attempts = 15;
    const int RetiredKept = 24;
    static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(100);

    // One capture cycle at a time: the snapshot forces a layout commit of the whole window, and a
    // tile another request removed mid-commit took UIKit's layout callbacks into freed peers.
    readonly SemaphoreSlim _gate = new(1, 1);

    // Removed tiles stay referenced for a while so their native peers outlive any pending callback.
    readonly Queue<View> _retired = new();

    /// <param name="itemId">The catalog item.</param>
    /// <param name="focused">The full-width focused instance is wanted (an unfocused item falls back to its tile).</param>
    public async Task<TilePreview> RenderAsync(string itemId, bool focused)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RenderCoreAsync(itemId, focused).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<TilePreview> RenderCoreAsync(string itemId, bool focused)
    {
        if (host.Catalog.SelectMany(s => s.Items).FirstOrDefault(i => i.Id == itemId) is not { } item)
            return Failed("unknown item");

        if (host.Focused is { } sample && sample.Item.Id == itemId)
            return await CaptureAsync(sample.Host, sample.Sample, sample.Error).ConfigureAwait(false);
        if (focused)
            return Failed("the item is not focused");

        if (host.Page is { } page)
        {
            var realized = await mainThread.RunAsync(() => page.FindRealized(itemId)).ConfigureAwait(false);
            if (realized != null)
                return await CaptureAsync(realized.Host, realized.Normal, realized.Error).ConfigureAwait(false);
        }

        var staged = await mainThread.RunAsync(() => StageTile(item)).ConfigureAwait(false);
        if (staged == null)
            return Failed("no window to render in");
        try
        {
            return await CaptureAsync(staged.Host, staged.Normal, staged.Error).ConfigureAwait(false);
        }
        finally
        {
            await mainThread.RunAsync(() =>
            {
                host.Stage.Remove(staged);
                Retire(staged);
                return true;
            }).ConfigureAwait(false);
        }
    }

    void Retire(View tile)
    {
        _retired.Enqueue(tile);
        while (_retired.Count > RetiredKept)
            _retired.Dequeue();
    }

    /// <summary>A tile at the width the device list would give it, on the off-screen stage.</summary>
    CookbookTile? StageTile(CookbookItem item)
    {
        if (!host.Stage.EnsureAttached(host.SampleContext))
            return null;
        var (_, width) = CookbookGridMetrics.For(item.Section, host.Stage.Width);
        var tile = new CookbookTile(item, width)
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            BindingContext = host.SampleContext,
        };
        CookbookBackdrop.Paint(tile.Host, MauiInspector.Options.Cookbook);
        host.Stage.Add(tile);
        return tile;
    }

    /// <summary>Waits for the host's first layout pass, then captures it.</summary>
    async Task<TilePreview> CaptureAsync(SampleHost host, View? sample, string? error)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var png = await mainThread.RunTaskAsync(() => TileCapture.CaptureAsync(host)).ConfigureAwait(false);
            if (png != null)
            {
                var states = await mainThread.RunAsync(() => VisualStates.NamesOf(sample)).ConfigureAwait(false);
                return new TilePreview(png, states, null);
            }
            await Task.Delay(Retry).ConfigureAwait(false);
        }
        return Failed(error ?? "the sample has no size yet");
    }

    static TilePreview Failed(string error) => new(null, [], error);
}
