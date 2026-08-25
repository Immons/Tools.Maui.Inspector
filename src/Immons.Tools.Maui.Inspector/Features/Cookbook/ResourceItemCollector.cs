namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>Turns every reachable resource dictionary entry into a cookbook item, by value type.</summary>
internal sealed class ResourceItemCollector(IResourceScopes scopes)
{
    public void Collect(CookbookItemSink sink)
    {
        foreach (var scope in scopes.All())
        {
            foreach (var (key, value) in scope.Entries())
                Describe(sink, scope, key, value);
        }
    }

    static void Describe(CookbookItemSink sink, ResourceScope scope, string key, object? value)
    {
        var source = scope.Label;
        switch (value)
        {
            case Color color:
                sink.Add(CookbookSections.Colors, Item(key, CookbookKinds.Color, source,
                    color.ToArgbHex(true), color.ToArgbHex(true), () => ResourceValueSample.Swatch(color))
                    with { LiveValue = () => CurrentHex(scope, key), RefreshSample = view => ResourceValueSample.Refresh(view, Current(scope, key)) });
                break;
            case SolidColorBrush brush:
                sink.Add(CookbookSections.Colors, Item(key, CookbookKinds.Brush, source,
                    brush.Color.ToArgbHex(true), brush.Color.ToArgbHex(true), () => ResourceValueSample.Swatch(brush))
                    with { LiveValue = () => CurrentHex(scope, key), RefreshSample = view => ResourceValueSample.Refresh(view, Current(scope, key)) });
                break;
            case GradientBrush gradient:
                sink.Add(CookbookSections.Colors, Item(key, CookbookKinds.Brush, source,
                    $"{gradient.GetType().Name} · {gradient.GradientStops.Count} stops", null, () => ResourceValueSample.Swatch(gradient)));
                break;
            case Style style:
                AddStyle(sink, key, style, source);
                break;
            case ControlTemplate template:
                sink.Add(CookbookSections.Templates, Item(key, CookbookKinds.ControlTemplate, source,
                    "ControlTemplate · placeholder content", null, () => TemplateSample.FromControlTemplate(template)));
                break;
            case DataTemplateSelector:
                break; // picks a template per item — nothing to render on its own
            case DataTemplate template:
                AddDataTemplate(sink, key, template, source);
                break;
            case Shadow shadow:
                sink.Add(CookbookSections.Scalars, Item(key, CookbookKinds.Shadow, source,
                    ElementCloner.XamlAttributeValue(shadow) ?? "Shadow", null, () => ResourceValueSample.Shadow(shadow)));
                break;
            case ImageSource image:
                sink.Add(CookbookSections.Images, Item(key, CookbookKinds.Image, source,
                    ValueFormatter.FormatValue(image), null, () => ResourceValueSample.Image(image)));
                break;
            case double or float or int or long or string or bool or Thickness or CornerRadius:
                var text = ValueFormatter.FormatValue(value);
                sink.Add(CookbookSections.Scalars, Item(key, CookbookKinds.Scalar, source,
                    $"{XamlTypeName(value)} · {text}", text, () => ResourceValueSample.Scalar(value)));
                break;
        }
    }

    static void AddStyle(CookbookItemSink sink, string key, Style style, string source)
    {
        var target = style.TargetType;
        var setters = $"{style.Setters.Count} setter{(style.Setters.Count == 1 ? "" : "s")}";

        if (key == target.FullName)
        {
            // Instantiable views show their implicit look in the Controls section; pages and
            // Shell cannot be hosted inside a page, so they are listed with their setters only.
            if (ControlSample.CanCreate(target))
                return;
            sink.Add(CookbookSections.Styles, Item(target.Name, CookbookKinds.Implicit, source, $"implicit · {setters}", null, null)
                with { TargetType = target.Name });
            return;
        }

        var basedOn = style.BasedOn is { } parent
            ? $" · based on {ResourceLookup.KeyOf(null, parent) ?? parent.TargetType.Name}"
            : "";
        var section = target == typeof(Label) || target == typeof(Span) ? CookbookSections.Typography : CookbookSections.Styles;
        var previewable = StyleSample.CanCreate(style);
        sink.Add(section, Item(key, CookbookKinds.Style, source, $"{target.Name} · {setters}{basedOn}", null,
                previewable ? () => StyleSample.Create(style) : null)
            with { TargetType = target.Name, HasStates = previewable && target != typeof(Span) });
    }

    static void AddDataTemplate(CookbookItemSink sink, string key, DataTemplate template, string source)
    {
        if (RecipeKey.TryParse(key, out var sectionId, out var sectionTitle, out var name))
        {
            sink.Add(sectionId, sectionTitle, Item(name, CookbookKinds.Recipe, source, key, null,
                () => TemplateSample.FromDataTemplate(template)) with { HasStates = true });
            return;
        }
        sink.Add(CookbookSections.Templates, Item(key, CookbookKinds.DataTemplate, source,
            "DataTemplate · no data context", null, () => TemplateSample.FromDataTemplate(template)));
    }

    static CookbookItem Item(string name, string kind, string source, string? detail, string? value, Func<View?>? sample) =>
        new("", "", name, kind, null, source, detail, value, sample);

    /// <summary>The entry as it is now — the Resources popup replaces Color/Brush entries on edit.</summary>
    static object? Current(ResourceScope scope, string key)
    {
        try
        {
            return scope.Dictionary.TryGetValue(key, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    static string? CurrentHex(ResourceScope scope, string key) => Current(scope, key) switch
    {
        Color color => color.ToArgbHex(true),
        SolidColorBrush brush => brush.Color.ToArgbHex(true),
        _ => null,
    };

    static string XamlTypeName(object value) => value switch
    {
        double => "x:Double",
        float => "x:Single",
        int => "x:Int32",
        long => "x:Int64",
        string => "x:String",
        bool => "x:Boolean",
        _ => value.GetType().Name,
    };
}
