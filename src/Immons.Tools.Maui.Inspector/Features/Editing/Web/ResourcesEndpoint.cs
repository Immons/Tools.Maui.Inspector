using System.Net;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Web.Http;

namespace Immons.Tools.Maui.Inspector.Features.Editing.Web;

/// <summary>
/// GET /api/resources — application- and page-level resource dictionaries (colors with
/// swatches, brushes, styles, scalars). POST /api/resources/set — live-edits a Color/Brush or
/// scalar (number, string, bool, Thickness, CornerRadius) resource in place and records the
/// new value for the XAML Updater. DynamicResource consumers update immediately; StaticResource
/// references were resolved at inflation time and keep their old value until the page is rebuilt.
/// </summary>
internal sealed class ResourcesEndpoint(
    IMainThreadDispatcher mainThread,
    IResourceScopes scopes,
    IXamlChangeLog xamlChanges,
    ICookbookHost cookbook) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Resources.List)
        {
            var json = await mainThread.RunAsync(BuildList).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Resources.SetSetter)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var key = node?["key"]?.GetValue<string>() ?? "";
            var property = node?["property"]?.GetValue<string>() ?? "";
            var value = node?["value"]?.GetValue<string>() ?? "";
            var result = await mainThread.RunAsync(() => SetStyleSetter(key, property, value)).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, new JsonObject
            {
                ["ok"] = result.Ok,
                ["recorded"] = result.Recorded,
            }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Resources.Set)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var key = node?["key"]?.GetValue<string>() ?? "";
            var value = node?["value"]?.GetValue<string>() ?? "";
            var result = await mainThread.RunAsync(() => SetResource(key, value)).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, new JsonObject
            {
                ["ok"] = result.Ok,
                ["recorded"] = result.Recorded,
            }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    string BuildList()
    {
        var groups = new JsonArray();
        foreach (var (name, dictionary, source) in scopes.All())
        {
            var entries = new JsonArray();
            foreach (var key in dictionary.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (!dictionary.TryGetValue(key, out var value))
                    continue;
                entries.Add(Describe(key, value));
            }
            if (entries.Count > 0)
            {
                groups.Add(new JsonObject
                {
                    ["name"] = name,
                    ["source"] = source,
                    ["entries"] = entries,
                });
            }
        }
        return new JsonObject { ["groups"] = groups }.ToJsonString();
    }

    JsonObject Describe(string key, object? value)
    {
        var entry = new JsonObject { ["key"] = key };
        switch (value)
        {
            case Color color:
                entry["kind"] = "color";
                entry["value"] = color.ToArgbHex(true);
                break;
            case SolidColorBrush brush:
                entry["kind"] = "brush";
                entry["value"] = brush.Color.ToArgbHex(true);
                break;
            case Style style:
                entry["kind"] = "style";
                entry["value"] = $"Style ({style.TargetType.Name}, {style.Setters.Count} setters)";
                entry["targetType"] = style.TargetType.Name;
                var setters = new JsonArray();
                foreach (var setter in style.Setters)
                {
                    if (setter.Property == null)
                        continue;
                    var text = setter.Value != null
                        ? Structure.ElementCloner.XamlAttributeValue(setter.Value)
                        : null;
                    // A value resolved from "{StaticResource X}" is shown as that reference —
                    // editing the literal here would replace the reference in the XAML.
                    var referenceKey = setter.Value != null ? KeyForValue(setter.Value) : null;
                    var setterJson = new JsonObject
                    {
                        ["property"] = setter.Property.PropertyName,
                        ["value"] = referenceKey != null
                            ? $"{{StaticResource {referenceKey}}}"
                            : text ?? setter.Value?.GetType().Name ?? "(null)",
                        ["editable"] = text != null || referenceKey != null,
                    };
                    if (referenceKey != null && setter.Value != null)
                    {
                        var referenced = Describe(referenceKey, setter.Value);
                        setterJson["resourceKey"] = referenceKey;
                        setterJson["resourceKind"] = referenced["kind"]?.GetValue<string>();
                        setterJson["resourceValue"] = referenced["value"]?.GetValue<string>();
                    }
                    setters.Add(setterJson);
                }
                entry["setters"] = setters;
                break;
            case double or float or int or long:
                entry["kind"] = "number";
                entry["value"] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case string text:
                entry["kind"] = "text";
                entry["value"] = text;
                break;
            case bool flag:
                entry["kind"] = "bool";
                entry["value"] = flag ? "True" : "False";
                break;
            case Thickness thickness:
                entry["kind"] = "thickness";
                entry["value"] = ValueFormatter.Format(thickness);
                break;
            case CornerRadius corner:
                entry["kind"] = "cornerradius";
                entry["value"] = ValueFormatter.Format(corner);
                break;
            case Shadow shadowValue:
                entry["kind"] = "shadow";
                entry["value"] = Structure.ElementCloner.XamlAttributeValue(shadowValue) ?? "Shadow";
                break;
            case null:
                entry["kind"] = "other";
                entry["value"] = "(null)";
                break;
            default:
                entry["kind"] = "other";
                entry["value"] = value.GetType().Name;
                break;
        }
        return entry;
    }

    /// <summary>Key under which this value lives in any reachable dictionary — reference
    /// identity first, then an unambiguous value-equal match of the same type.</summary>
    string? KeyForValue(object value)
    {
        string? valueMatch = null;
        var ambiguous = false;
        foreach (var (_, dictionary, _) in scopes.All())
        {
            foreach (var key in dictionary.Keys)
            {
                if (!dictionary.TryGetValue(key, out var candidate) || candidate == null)
                    continue;
                if (ReferenceEquals(candidate, value))
                    return key;
                if (candidate.GetType() == value.GetType() && Equals(candidate, value))
                {
                    if (valueMatch != null && valueMatch != key)
                        ambiguous = true;
                    else
                        valueMatch = key;
                }
            }
        }
        return ambiguous ? null : valueMatch;
    }

    /// <summary>Re-types the entered text to match the resource's current value.</summary>
    (bool Ok, bool Recorded) SetResource(string key, string text)
    {
        foreach (var (_, dictionary, source) in scopes.All())
        {
            if (!dictionary.TryGetValue(key, out var existing))
                continue;

            var parsed = ParseAs(existing, text);
            if (parsed == null)
                return (false, false);

            // A mutable resource keeps its identity — consumers and setters update by
            // themselves. Immutable ones are replaced, and setters that pointed at the old
            // instance are re-pointed so their "{StaticResource}" mapping (and live look)
            // survive the edit.
            if (existing is Shadow oldShadow && parsed is Shadow newShadow)
            {
                oldShadow.Brush = newShadow.Brush;
                oldShadow.Offset = newShadow.Offset;
                oldShadow.Radius = newShadow.Radius;
                oldShadow.Opacity = newShadow.Opacity;
            }
            else
            {
                dictionary[key] = parsed;
                RepointSetters(existing, parsed);
            }

            var recorded = xamlChanges.Enabled && !string.IsNullOrEmpty(source);
            xamlChanges.RecordResourceValue(source, key, text.Trim());
            cookbook.RefreshSamples();
            return (true, recorded);
        }
        return (false, false);
    }

    /// <summary>Setters resolved from the replaced instance get the new one, live.</summary>
    void RepointSetters(object? oldValue, object newValue)
    {
        if (oldValue == null)
            return;
        foreach (var (_, dictionary, _) in scopes.All())
        {
            foreach (var key in dictionary.Keys)
            {
                if (!dictionary.TryGetValue(key, out var resource) || resource is not Style style)
                    continue;
                var touched = false;
                foreach (var setter in style.Setters)
                {
                    if (ReferenceEquals(setter.Value, oldValue))
                    {
                        setter.Value = newValue;
                        touched = true;
                    }
                }
                if (touched)
                    ReapplyStyle(style);
            }
        }
    }

    static object? ParseAs(object? existing, string text)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        switch (existing)
        {
            case Color:
                return ValueParser.ParseColorValue(text);
            case SolidColorBrush:
                return ValueParser.ParseColorValue(text) is { } color ? new SolidColorBrush(color) : null;
            case double:
                return double.TryParse(text, System.Globalization.NumberStyles.Float, invariant, out var d) ? d : null;
            case float:
                return float.TryParse(text, System.Globalization.NumberStyles.Float, invariant, out var f) ? f : null;
            case int:
                return int.TryParse(text, out var i) ? i : null;
            case long:
                return long.TryParse(text, out var l) ? l : null;
            case bool:
                return bool.TryParse(text, out var b) ? b : null;
            case string:
                return text;
            case Thickness:
                return ValueParser.TryParse(typeof(Thickness), EditorKind.Thickness, text, out var t) ? t : null;
            case CornerRadius:
                return ValueParser.TryParse(typeof(CornerRadius), EditorKind.CornerRadius, text, out var c) ? c : null;
            case Shadow:
                try
                {
                    return new ShadowTypeConverter().ConvertFromInvariantString(text) as Shadow;
                }
                catch
                {
                    return null;
                }
            default:
                return null;
        }
    }

    /// <summary>
    /// Edits one setter of a keyed (or implicit, key = type full name) style: parses the value
    /// against the property's type, re-applies the style to its live consumers, and records the
    /// change for the XAML Updater — anchored by style key, not by line numbers.
    /// </summary>
    (bool Ok, bool Recorded) SetStyleSetter(string key, string propertyName, string text)
    {
        foreach (var (_, dictionary, source) in scopes.All())
        {
            if (!dictionary.TryGetValue(key, out var resource) || resource is not Style style)
                continue;

            var setter = style.Setters.FirstOrDefault(s => s.Property?.PropertyName == propertyName);
            var bindable = setter?.Property
                ?? ReflectionLookup.FindBindableProperty(style.TargetType, propertyName);
            if (bindable == null)
                return (false, false);

            object? parsed;
            if (ResourceResolver.IsResourceReference(text, out var referenceKey))
            {
                // Re-pointing the setter at another resource: resolve, coerce, record verbatim.
                object? referenced = null;
                foreach (var (_, candidates, _) in scopes.All())
                {
                    if (candidates.TryGetValue(referenceKey, out referenced) && referenced != null)
                        break;
                }
                if (referenced == null
                    || ResourceResolver.Coerce(referenced, bindable.ReturnType) is not { } coerced)
                    return (false, false);
                parsed = coerced;
            }
            else if (!ValueParser.TryParse(bindable.ReturnType, Structure.AttributeApplier.KindOf(bindable.ReturnType), text, out parsed) || parsed == null)
            {
                return (false, false);
            }

            if (setter == null)
                style.Setters.Add(new Setter { Property = bindable, Value = parsed });
            else
                setter.Value = parsed;

            ReapplyStyle(style);
            var recorded = xamlChanges.Enabled && !string.IsNullOrEmpty(source);
            xamlChanges.RecordStyleSetter(source, key, style.TargetType.Name, propertyName, text);
            return (true, recorded);
        }
        return (false, false);
    }

    /// <summary>
    /// Setters are applied on attach — re-assign the style so live consumers refresh.
    /// Walks every page alive in the app (all windows, full navigation and modal stacks),
    /// not just the visible one; pages built later pick the new setter up by themselves.
    /// </summary>
    static void ReapplyStyle(Style style)
    {
        void Walk(VisualElement element)
        {
            if (element is View view && ReferenceEquals(view.Style, style))
            {
                view.Style = null;
                view.Style = style;
            }
            foreach (var child in Features.VisualTree.VisualTreeWalker.GetVisualChildren(element))
                Walk(child);
        }

        foreach (var page in AllLivePages())
            Walk(page);
    }

    /// <summary>Every instantiated page: window roots, container contents, nav and modal stacks.</summary>
    static IEnumerable<Page> AllLivePages()
    {
        var seen = new HashSet<Page>();

        void Add(Page? page)
        {
            if (page == null || !seen.Add(page))
                return;
            switch (page)
            {
                case Shell shell:
                    Add(shell.CurrentPage);
                    break;
                case NavigationPage navigation:
                    foreach (var stacked in navigation.Navigation?.NavigationStack ?? [])
                        Add(stacked);
                    Add(navigation.CurrentPage);
                    break;
                case FlyoutPage flyout:
                    Add(flyout.Flyout);
                    Add(flyout.Detail);
                    break;
                case TabbedPage tabbed:
                    foreach (var child in tabbed.Children)
                        Add(child);
                    break;
            }
            try
            {
                foreach (var stacked in page.Navigation?.NavigationStack ?? [])
                    Add(stacked);
            }
            catch
            {
                // navigation may be unavailable mid-teardown
            }
        }

        foreach (var window in Application.Current?.Windows ?? [])
        {
            if (window.Page is not { } root)
                continue;
            Add(root);
            IReadOnlyList<Page>? modals = null;
            try { modals = root.Navigation?.ModalStack; }
            catch { /* navigation may be unavailable mid-teardown */ }
            foreach (var modal in modals ?? [])
                Add(modal);
        }
        return seen;
    }
}
