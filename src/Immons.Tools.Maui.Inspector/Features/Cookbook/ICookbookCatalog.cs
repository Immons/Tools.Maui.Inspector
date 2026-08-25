namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Discovers everything worth a tile — resources, fonts, controls, images — grouped into sections.</summary>
internal interface ICookbookCatalog
{
    /// <summary>Builds a fresh catalog from the app's current state. Main thread (walks resources).</summary>
    IReadOnlyList<CookbookSection> Build();
}
