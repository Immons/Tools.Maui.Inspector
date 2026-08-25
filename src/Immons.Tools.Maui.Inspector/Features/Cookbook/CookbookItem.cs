namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>One cookbook entry: what it is, where it comes from and how to build its live sample.</summary>
/// <param name="Id">Stable, URL-safe id (unique within a catalog).</param>
/// <param name="Section">Section id (see <see cref="CookbookSections"/>).</param>
/// <param name="Name">Resource key, type name or font alias.</param>
/// <param name="Kind">See <see cref="CookbookKinds"/>.</param>
/// <param name="TargetType">Style/template target or control type name.</param>
/// <param name="Source">Dictionary file, page or assembly the entry comes from.</param>
/// <param name="Detail">One-line description shown under the sample.</param>
/// <param name="Value">A value the web client can render by itself (a color hex, a scalar).</param>
/// <param name="LiveValue">Re-reads <paramref name="Value"/> from the dictionary — resource edits replace the entry, the catalog snapshot must not go stale.</param>
/// <param name="RefreshSample">Re-reads the resource into the live sample after an edit (a replaced Color/Brush entry does not reach a built tile by itself).</param>
/// <param name="CreateSample">Builds the sample view; null when the entry has no visual form.</param>
/// <param name="HasStates">Control-like samples: a Disabled variant is rendered and visual states can be forced.</param>
internal sealed record CookbookItem(
    string Id,
    string Section,
    string Name,
    string Kind,
    string? TargetType,
    string? Source,
    string? Detail,
    string? Value,
    Func<View?>? CreateSample,
    bool HasStates = false,
    Func<string?>? LiveValue = null,
    Action<View>? RefreshSample = null);
