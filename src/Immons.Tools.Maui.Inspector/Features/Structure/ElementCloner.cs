using System.Globalization;
using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Structure;

/// <summary>
/// Deep-copies an element for paste: same type, the scalar/visual property values that differ
/// from the type's defaults, and recursively cloned children. Alongside the live clone it
/// produces the XAML form of those properties (for the insert snippet) and, when the whole
/// subtree is expressible, the nested children markup.
/// </summary>
internal static class ElementCloner
{
    static readonly HashSet<string> SkippedProperties =
    [
        "Parent", "Handler", "Window", "BindingContext", "StyleId", "AutomationId", "Id",
        "ClassId", "Content", "Children", "ItemsSource", "Resources", "Behaviors", "Triggers",
        "GestureRecognizers", "Clip",
        // Runtime state, not XAML-able configuration.
        "IsPlatformEnabled", "IsFocused", "IsLoaded",
    ];

    internal sealed record CloneResult(
        View Element, Dictionary<string, string> Attributes, string? ChildrenXml,
        Dictionary<string, string>? XmlnsMap);

    /// <summary>
    /// Serializes a LIVE subtree (no cloning): the root's non-default attributes and, when the
    /// whole tree is expressible in the default namespace, the nested children markup. Used to
    /// refresh a pasted/added element's insert snippet after any edit inside it.
    /// </summary>
    public static (Dictionary<string, string> Attributes, string? ChildrenXml, Dictionary<string, string>? XmlnsMap)? Describe(View source, bool deepCustom = false)
    {
        View baseline;
        try
        {
            baseline = (View)Activator.CreateInstance(source.GetType())!;
        }
        catch
        {
            return null;
        }

        var xmlns = new Dictionary<string, string>();
        var attributes = DiffAttributes(source, baseline, applyTo: null);
        var childrenXml = DescribeChildren(source, xmlns, deepCustom);
        return (attributes, childrenXml, xmlns.Count > 0 ? xmlns : null);
    }

    static string? DescribeChildren(View source, Dictionary<string, string> xmlns, bool deepCustom)
    {
        var childViews = ChildViewsOf(source, deepCustom);
        if (childViews.Count == 0)
            return null;

        var lines = new List<string>();
        foreach (var child in childViews)
        {
            View baseline;
            try
            {
                baseline = (View)Activator.CreateInstance(child.GetType())!;
            }
            catch
            {
                continue; // not recreatable — cannot appear in the file either
            }
            var attrs = DiffAttributes(child, baseline, applyTo: null);
            lines.Add(RenderXml(TagName(child.GetType(), xmlns), attrs, DescribeChildren(child, xmlns, deepCustom)));
        }
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    /// <summary>
    /// Built-ins render bare; anything else gets a local placeholder prefix (p1, p2, …) that
    /// the sync tool maps onto the file's real xmlns declarations.
    /// </summary>
    static string TagName(Type type, Dictionary<string, string> xmlns)
    {
        if (type.Namespace?.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal) == true)
            return type.Name;

        var declaration = $"clr-namespace:{type.Namespace};assembly={type.Assembly.GetName().Name}";
        var prefix = xmlns.FirstOrDefault(kv => kv.Value == declaration).Key;
        if (prefix == null)
        {
            prefix = $"p{xmlns.Count + 1}";
            xmlns[prefix] = declaration;
        }
        return $"{prefix}:{type.Name}";
    }

    static List<View> ChildViewsOf(View source, bool deepCustom) => source switch
    {
        // A custom control OWNS its visual body (built by its own ctor/InitializeComponent):
        // cloning or serializing it would duplicate the body on top of the fresh instance's own.
        // Custom types are leaves — attributes only — unless the user forces a deep copy.
        _ when !deepCustom
            && source.GetType().Namespace?.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal) != true
            => new List<View>(),
        Layout layout => layout.Children.OfType<View>().ToList(),
        Border { Content: View b } => new List<View> { b },
        ScrollView { Content: View s } => new List<View> { s },
        ContentView { Content: View c } => new List<View> { c },
        _ => new List<View>(),
    };

    /// <summary>
    /// The XAML the serializer would write for this element — shown as a read-only preview.
    /// Custom controls appear as leaves with their attributes, prefixes resolved inline.
    /// </summary>
    public static string Preview(View element)
    {
        var described = Describe(element);
        if (described == null)
            return $"<!-- {element.GetType().Name}: no parameterless constructor — not serializable -->";

        var xmlns = described.Value.XmlnsMap ?? new Dictionary<string, string>();
        var tag = TagName(element.GetType(), xmlns);
        var attributes = new Dictionary<string, string>(described.Value.Attributes);
        foreach (var (prefix, declaration) in xmlns)
            attributes[$"xmlns:{prefix}"] = declaration;
        return RenderXml(tag, attributes, described.Value.ChildrenXml);
    }

    /// <summary>Null when the element's type cannot be recreated (no parameterless ctor).</summary>
    public static CloneResult? Clone(View source, bool deepCustom = false)
    {
        var type = source.GetType();
        View clone;
        View baseline;
        try
        {
            clone = (View)Activator.CreateInstance(type)!;
            baseline = (View)Activator.CreateInstance(type)!;
        }
        catch
        {
            return null;
        }

        var xmlns = new Dictionary<string, string>();
        var attributes = DiffAttributes(source, baseline, applyTo: clone);
        var childrenXml = CloneChildren(source, clone, xmlns, deepCustom);
        return new CloneResult(clone, attributes, childrenXml, xmlns.Count > 0 ? xmlns : null);
    }

    static Dictionary<string, string> DiffAttributes(View source, View baseline, View? applyTo)
    {
        var attributes = new Dictionary<string, string>();
        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true }
                || property.GetMethod is not { IsPublic: true }
                || property.GetIndexParameters().Length > 0
                || SkippedProperties.Contains(property.Name)
                || !IsCopyable(property.PropertyType))
                continue;

            try
            {
                var value = property.GetValue(source);
                if (Equals(value, property.GetValue(baseline)))
                    continue;

                if (applyTo != null)
                    property.SetValue(applyTo, value);
                if (value != null && XamlValue(value) is { } text)
                    attributes[property.Name] = text;
            }
            catch
            {
                // One stubborn property must not break the paste.
            }
        }
        return attributes;
    }

    static bool IsCopyable(Type type) =>
        type.IsValueType
        || type == typeof(string)
        || type == typeof(Color)
        || type == typeof(Style)
        || typeof(ImageSource).IsAssignableFrom(type);

    /// <summary>Clones children into the clone and renders their markup (custom types via prefixes).</summary>
    static string? CloneChildren(View source, View clone, Dictionary<string, string> xmlns, bool deepCustom)
    {
        var childViews = ChildViewsOf(source, deepCustom);
        if (childViews.Count == 0)
            return null;

        var lines = new List<string>();
        foreach (var child in childViews)
        {
            if (Clone(child, deepCustom) is not { } childClone)
                continue;
            if (ElementAttacher.Attach(clone, childClone.Element) != null)
                continue;

            var nestedXml = childClone.ChildrenXml;
            // Merge the child's own prefixes into the shared map (rendered text uses ours).
            lines.Add(RenderXml(TagName(child.GetType(), xmlns), childClone.Attributes,
                MergePrefixes(nestedXml, childClone.XmlnsMap, xmlns)));
        }

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    /// <summary>Rewrites a nested block's local prefixes onto the shared map's prefixes.</summary>
    static string? MergePrefixes(string? xml, Dictionary<string, string>? local, Dictionary<string, string> shared)
    {
        if (xml == null || local == null)
            return xml;

        foreach (var (localPrefix, declaration) in local)
        {
            var prefix = shared.FirstOrDefault(kv => kv.Value == declaration).Key;
            if (prefix == null)
            {
                prefix = $"p{shared.Count + 1}";
                shared[prefix] = declaration;
            }
            if (prefix != localPrefix)
                xml = xml.Replace($"<{localPrefix}:", $"<{prefix}:").Replace($"</{localPrefix}:", $"</{prefix}:");
        }
        return xml;
    }

    static string RenderXml(string tagName, Dictionary<string, string> attributes, string? childrenXml)
    {
        var parts = new List<string> { tagName };
        foreach (var (name, value) in attributes)
            parts.Add($"{name}=\"{Escape(value)}\"");
        var open = string.Join(' ', parts);

        if (childrenXml == null)
            return $"<{open} />";

        var indented = string.Join("\n", childrenXml.Split('\n').Select(line => "    " + line));
        return $"<{open}>\n{indented}\n</{tagName}>";
    }

    static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    /// <summary>The XAML attribute form of a property value; null when it has none.</summary>
    internal static string? XamlAttributeValue(object value) => XamlValue(value);

    static string? XamlValue(object value) => value switch
    {
        string s => s,
        bool b => b ? "True" : "False",
        Color c => c.ToArgbHex(true),
        Thickness t => string.Create(CultureInfo.InvariantCulture, $"{t.Left},{t.Top},{t.Right},{t.Bottom}"),
        CornerRadius r => string.Create(CultureInfo.InvariantCulture, $"{r.TopLeft},{r.TopRight},{r.BottomLeft},{r.BottomRight}"),
        LayoutOptions lo => lo.Alignment switch
        {
            LayoutAlignment.Start => "Start",
            LayoutAlignment.Center => "Center",
            LayoutAlignment.End => "End",
            _ => "Fill",
        },
        Enum e => e.ToString(),
        // ShadowTypeConverter's 5-token form: "offsetX offsetY radius color opacity".
        Shadow { Brush: SolidColorBrush { Color: { } shadowColor } } shadow => string.Create(
            CultureInfo.InvariantCulture,
            $"{shadow.Offset.X} {shadow.Offset.Y} {shadow.Radius} {shadowColor.ToArgbHex(true)} {shadow.Opacity}"),
        FileImageSource file => file.File,
        UriImageSource uriSource => uriSource.Uri?.ToString(),
        IConvertible c => c.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };
}
