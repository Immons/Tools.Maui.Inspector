namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// The Controls section: every toolbox control (MAUI built-ins and the app's own) with its
/// implicit look, plus the targets of implicit styles the toolbox does not list.
/// </summary>
internal sealed class ControlItemCollector(IElementCatalog catalog, IResourceScopes scopes, CookbookOptions options)
{
    /// <summary>Embedded browsers are too heavy — and too blank — to be worth a tile by default.</summary>
    static readonly HashSet<Type> HeavyByDefault = [typeof(WebView)];

    readonly ControlFilter _filter = new(options);

    public void Collect(CookbookItemSink sink)
    {
        var implicitStyles = ImplicitStyles();
        var listed = new HashSet<string>();

        foreach (var entry in catalog.All())
        {
            if (catalog.Resolve(entry.TypeName) is not { } type || IsExcluded(type))
                continue;
            listed.Add(entry.TypeName);
            implicitStyles.TryGetValue(entry.TypeName, out var implicitStyle);
            Add(sink, type, entry.IsCustom, implicitStyle.Source);
        }

        // RadioButton, IndicatorView, RefreshView… are not toolbox entries but their implicit
        // style still deserves a tile.
        foreach (var (typeName, (type, source)) in implicitStyles)
        {
            if (listed.Contains(typeName) || !ControlSample.CanCreate(type) || IsExcluded(type))
                continue;
            Add(sink, type, isCustom: type.Namespace?.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal) != true, source);
        }
    }

    static void Add(CookbookItemSink sink, Type type, bool isCustom, string? implicitStyleSource)
    {
        var detail = (isCustom ? $"custom · {type.Namespace}" : "")
            + (implicitStyleSource != null ? (isCustom ? " · " : "") + $"implicit style · {implicitStyleSource}" : isCustom ? "" : "no implicit style");
        sink.Add(CookbookSections.Controls, new CookbookItem("", "", type.Name,
            isCustom ? CookbookKinds.Custom : CookbookKinds.Control, type.Name,
            implicitStyleSource ?? type.Assembly.GetName().Name, detail, null,
            () => ControlSample.Create(type), HasStates: true));
    }

    /// <summary>Type full name → (target type, dictionary file) for every implicit style reachable.</summary>
    Dictionary<string, (Type Type, string Source)> ImplicitStyles()
    {
        var result = new Dictionary<string, (Type, string)>();
        foreach (var scope in scopes.All())
        {
            foreach (var (key, value) in scope.Entries())
            {
                if (value is Style style && key == style.TargetType.FullName)
                    result.TryAdd(key, (style.TargetType, scope.Label));
            }
        }
        return result;
    }

    bool IsExcluded(Type type) => HeavyByDefault.Contains(type) || !_filter.Allows(type);
}
