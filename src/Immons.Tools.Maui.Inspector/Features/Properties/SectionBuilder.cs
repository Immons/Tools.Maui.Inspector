namespace Immons.Tools.Maui.Inspector.Features.Properties;

/// <summary>Shared helpers for the section builders.</summary>
internal static class SectionBuilder
{
    public static PropertySection New(string title, string? group = null) => new(title, [], group);

    public static void AddIfAny(List<PropertySection> sections, PropertySection section)
    {
        if (section.Rows.Count > 0)
            sections.Add(section);
    }

    public static void Add(PropertySection s, string name, string value, Color? swatch = null) =>
        s.Rows.Add(new PropertyRow(name, value, swatch));

    /// <summary>
    /// Adds a row backed by a public CLR property of the element; the row becomes editable
    /// when the property has a public setter of a supported type.
    /// </summary>
    public static void AddEditable(PropertySection s, object el, string property, string? label = null,
        string? value = null, string? note = null)
    {
        var pi = ReflectionLookup.FindInstanceProperty(el.GetType(), property);
        if (pi == null)
            return;

        object? raw = null;
        try { raw = pi.GetValue(el); }
        catch { /* getter threw — show the row as empty */ }

        var (sourceExpression, sourceReference) = SourceMarkup(el, property);
        s.Rows.Add(new PropertyRow(
            label ?? property,
            value ?? ValueFormatter.FormatValue(raw),
            raw as Color,
            EditorFactory.Clr(el, property),
            Binding: BindingDescriptor.Describe(el, property),
            DeviceExpression: InspectorServices.Current.Expressions.Find(el, property) ?? sourceExpression,
            Resources: SuggestionsFor(el, property, Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType),
            Note: note ?? sourceReference));
    }

    /// <summary>
    /// XAML-authored "{OnIdiom …}"/"{StaticResource …}" are resolved at parse time and leave
    /// no runtime trace — the raw attribute read from the embedded source shows the truth.
    /// Per-device expressions feed the ⋔ badge; resource references the origin note.
    /// </summary>
    static (string? DeviceExpression, string? ResourceReference) SourceMarkup(object el, string property)
    {
        var text = XamlSourceText.AttributeText(el, property)?.Trim();
        if (text == null)
            return (null, null);
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\{\s*(?:\w+:)?(OnIdiom|OnPlatform|Adaptive)\b"))
            return (text, null);
        if (text.StartsWith("{StaticResource", StringComparison.Ordinal)
            || text.StartsWith("{DynamicResource", StringComparison.Ordinal))
            return (null, text);
        return (null, null);
    }

    /// <summary>
    /// Ready-to-use editor suggestions for a property: registered font aliases for FontFamily,
    /// otherwise "{StaticResource Key}" for resources whose value fits the property type.
    /// </summary>
    static IReadOnlyList<string> SuggestionsFor(object target, string property, Type propertyType)
    {
        if (property.Contains("FontFamily", StringComparison.Ordinal))
        {
            var fonts = FontCatalog.RegisteredAliases();
            return fonts.Count > 0 ? fonts : [];
        }

        return ResourceLookup.CompatibleKeys(target, propertyType)
            .Select(key => $"{{StaticResource {key}}}")
            .ToList();
    }

    /// <summary>Editable integer row backed by get/set delegates (attached properties like Grid.Row).</summary>
    public static void AddAttachedInt(PropertySection s, string name, Func<int> get, Action<int> set, int min, object target)
    {
        var editor = new PropertyEditor(EditorKind.Double, null, text =>
        {
            if (!int.TryParse(text.Trim(), out var value) || value < min)
                return false;
            try
            {
                set(value);
                return true;
            }
            catch
            {
                return false;
            }
        })
        {
            XamlTarget = target,
            XamlAttribute = name,
        };
        s.Rows.Add(new PropertyRow(name, get().ToString(), null, editor));
    }
}
