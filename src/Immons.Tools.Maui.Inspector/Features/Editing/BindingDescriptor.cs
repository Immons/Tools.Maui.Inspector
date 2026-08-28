using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>
/// Detects an active data binding on a bindable property and renders it as XAML-like text
/// ("{Binding Title}"). Editors use this to warn the user and to keep the sync tool
/// from overwriting a binding expression with a literal runtime value.
/// </summary>
internal static class BindingDescriptor
{
    // BindableObject keeps bindings in private per-property contexts — reached by reflection.
    static readonly MethodInfo? GetContextMethod = typeof(BindableObject)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .FirstOrDefault(m => m.Name == "GetContext"
            && m.GetParameters() is [{ ParameterType.Name: nameof(BindableProperty) }]);

    public static string? Describe(object target, string propertyName)
    {
        if (target is not BindableObject bindable)
            return null;
        var property = ReflectionLookup.FindBindableProperty(target.GetType(), propertyName);
        if (property == null)
            return null;
        return GetBinding(bindable, property) is { } binding ? Format(binding) : null;
    }

    static BindingBase? GetBinding(BindableObject bindable, BindableProperty property)
    {
        try
        {
            var context = GetContextMethod?.Invoke(bindable, [property]);
            if (context == null)
                return null;
            var type = context.GetType();
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Older storage: a single "Binding" member.
            if (ReadMember(context, type, "Binding", any) is BindingBase single)
                return single;

            // Newer storage: "Bindings" is a SetterSpecificityList<BindingBase> —
            // its parameterless GetValue() returns the effective binding.
            if (ReadMember(context, type, "Bindings", any) is { } bindings)
            {
                var getValue = bindings.GetType().GetMethod("GetValue", any, Type.EmptyTypes);
                return getValue?.Invoke(bindings, null) as BindingBase;
            }

            return null;
        }
        catch
        {
            return null; // internals changed — treat as unbound
        }
    }

    static object? ReadMember(object target, Type type, string name, BindingFlags flags) =>
        (object?)type.GetField(name, flags)?.GetValue(target)
        ?? type.GetProperty(name, flags)?.GetValue(target);

    static string Format(BindingBase binding)
    {
        if (binding is Binding b)
        {
            var text = $"{{Binding {b.Path}";
            if (b.Mode != BindingMode.Default)
                text += $", Mode={b.Mode}";
            if (b.Converter != null)
                text += $", Converter={b.Converter.GetType().Name}";
            if (!string.IsNullOrEmpty(b.StringFormat))
                text += $", StringFormat='{b.StringFormat}'";
            if (b.Source != null)
                text += $", Source={b.Source.GetType().Name}";
            return text + "}";
        }

        // Compiled bindings (x:DataType) are TypedBinding<TSource, TProperty> — the XAML said
        // "{Binding Path}", so render that, with the path rebuilt from the handler part names.
        if (binding is Microsoft.Maui.Controls.Internals.TypedBindingBase typed)
        {
            var text = $"{{Binding {TypedPath(typed) ?? "(compiled)"}";
            if (typed.Mode != BindingMode.Default)
                text += $", Mode={typed.Mode}";
            if (typed.Converter != null)
                text += $", Converter={typed.Converter.GetType().Name}";
            if (!string.IsNullOrEmpty(typed.StringFormat))
                text += $", StringFormat='{typed.StringFormat}'";
            if (typed.Source != null)
                text += $", Source={typed.Source.GetType().Name}";
            return text + "}";
        }

        var name = binding.GetType().Name;
        var backtick = name.IndexOf('`');
        return $"{{{(backtick > 0 ? name[..backtick] : name)}}}";
    }

    /// <summary>"A.B.C" from TypedBinding's private part-change handlers; null for OneTime
    /// bindings compiled without them.</summary>
    static string? TypedPath(BindingBase binding)
    {
        try
        {
            if (binding.GetType().GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(binding) is not Array handlers || handlers.Length == 0)
                return null;

            var parts = new List<string>();
            foreach (var handler in handlers)
            {
                if (handler?.GetType().GetProperty("PropertyName")?.GetValue(handler) is string part
                    && part.Length > 0)
                    parts.Add(part);
            }
            return parts.Count > 0 ? string.Join(".", parts) : null;
        }
        catch
        {
            return null; // internals changed — the "(compiled)" fallback still reads fine
        }
    }
}
