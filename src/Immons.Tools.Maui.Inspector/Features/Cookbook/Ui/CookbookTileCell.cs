namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

/// <summary>
/// The list's recycled cell: builds the tile for the item it is bound to and tells the page
/// which tiles are on screen right now (the only ones the inspector can select or force states on).
/// </summary>
internal sealed class CookbookTileCell(
    Func<double> tileWidth,
    Func<object?> sampleContext,
    Action<CookbookTile> realized,
    Action<CookbookTile> recycled) : ContentView
{
    CookbookTile? _tile;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        var item = BindingContext as CookbookItem;
        if (_tile != null && ReferenceEquals(_tile.Item, item))
            return;

        if (_tile != null)
        {
            recycled(_tile);
            _tile = null;
            Content = null;
        }
        if (item == null)
            return;

        _tile = new CookbookTile(item, tileWidth());
        // Samples get the app-provided context (or none) — never the catalog entry the cell is
        // bound to, or a recipe's {Binding Name} would happily resolve against it.
        _tile.BindingContext = sampleContext();
        Content = _tile;
        realized(_tile);
    }
}
