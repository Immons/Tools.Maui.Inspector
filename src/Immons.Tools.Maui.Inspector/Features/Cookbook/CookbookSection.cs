namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>An ordered group of items shown as one chip on the device and one heading on the web.</summary>
internal sealed record CookbookSection(string Id, string Title, IReadOnlyList<CookbookItem> Items);
