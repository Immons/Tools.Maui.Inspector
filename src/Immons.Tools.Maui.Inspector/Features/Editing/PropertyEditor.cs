namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>Attached to a PropertyRow to make it editable in the panel.</summary>
internal sealed class PropertyEditor(EditorKind kind, IReadOnlyList<string>? choices, Func<string, bool> apply)
{
    public EditorKind Kind { get; } = kind;
    public IReadOnlyList<string>? Choices { get; } = choices;

    /// <summary>Object whose XAML source location identifies the tag to patch (element, Span, Shadow…).</summary>
    public object? XamlTarget { get; init; }

    /// <summary>XAML attribute name to write ("FontSize", "Grid.Row", …).</summary>
    public string? XamlAttribute { get; init; }

    /// <summary>Maps the entered text to the XAML attribute value; return null to skip recording.</summary>
    public Func<string, string?>? XamlValue { get; init; }

    /// <summary>Resets the property to its default/style value (ClearValue). Null when unsupported.</summary>
    public Func<bool>? ClearAction { get; init; }

    public bool CanClear => ClearAction != null;

    /// <summary>Clears the property and records an attribute removal for the sync tool.</summary>
    public bool Clear()
    {
        if (ClearAction == null)
            return false;

        bool ok;
        try
        {
            ok = ClearAction();
        }
        catch
        {
            return false;
        }

        if (ok && XamlTarget != null && XamlAttribute != null)
        {
            // XamlValue may veto the removal (e.g. clearing a bound property must not
            // delete the "{Binding …}" attribute from the source file).
            var value = XamlValue == null ? XamlChangeLog.RemoveMarker : XamlValue(XamlChangeLog.RemoveMarker);
            if (value != null)
                InspectorServices.Current.XamlChanges.Record(XamlTarget, XamlAttribute, value);
        }

        return ok;
    }

    public bool Apply(string text)
    {
        bool ok;
        try
        {
            ok = apply(text);
        }
        catch
        {
            return false;
        }

        if (ok && XamlTarget != null && XamlAttribute != null)
        {
            var xamlValue = XamlValue != null ? XamlValue(text) : text.Trim();
            if (xamlValue != null)
                InspectorServices.Current.XamlChanges.Record(XamlTarget, XamlAttribute, xamlValue);
        }

        return ok;
    }
}
