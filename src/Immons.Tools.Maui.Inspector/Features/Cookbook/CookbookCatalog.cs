namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Composes the collectors; the sink orders sections and keeps item ids unique.</summary>
internal sealed class CookbookCatalog(IResourceScopes scopes, IElementCatalog controls) : ICookbookCatalog
{
    public IReadOnlyList<CookbookSection> Build()
    {
        var sink = new CookbookItemSink(new ResourceFilter(MauiInspector.Options.Cookbook));
        new ResourceItemCollector(scopes).Collect(sink);
        FontItemCollector.Collect(sink);
        new ControlItemCollector(controls, scopes, MauiInspector.Options.Cookbook).Collect(sink);
        ImageItemCollector.Collect(sink);
        return sink.ToSections();
    }
}
