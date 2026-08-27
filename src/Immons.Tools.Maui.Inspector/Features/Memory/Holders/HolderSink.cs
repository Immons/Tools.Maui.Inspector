using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Holders;

/// <summary>
/// Judges one field's value against the suspects: a delegate's targets (and the closure they may
/// sit in), a direct reference, or the items of a collection. Weak references and weak tables are
/// not holders and are skipped.
/// </summary>
internal sealed class HolderSink(Dictionary<object, int> ids, Dictionary<int, List<string>> found)
{
    const int MaxCollectionItems = 2000;

    public void Inspect(FieldInfo field, object? instance, string prefix, string owner)
    {
        object? value;
        try
        {
            value = field.GetValue(instance);
        }
        catch
        {
            return; // a static constructor that throws, a field this runtime will not read
        }
        if (value == null || value is string or WeakReference || IsWeakGeneric(value.GetType()))
            return;

        var name = FieldName(field);
        if (value is Delegate handler)
        {
            foreach (var target in handler.GetInvocationList())
                Judge(target.Target, $"{prefix}event {owner}.{name} → {target.Method.Name}", closure: true);
            return;
        }
        if (Judge(value, $"{prefix}field {owner}.{name}", closure: false))
            return;
        if (value is IEnumerable enumerable)
            InspectItems(enumerable, $"{prefix}collection {owner}.{name}");
    }

    bool Judge(object? candidate, string description, bool closure)
    {
        if (candidate == null)
            return false;
        if (ids.TryGetValue(candidate, out var id))
        {
            Add(id, description);
            return true;
        }
        // A lambda's target is its closure; the suspect may sit in one of its fields.
        if (closure && candidate.GetType().Name.Contains("DisplayClass", StringComparison.Ordinal))
        {
            foreach (var inner in candidate.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object? captured;
                try
                {
                    captured = inner.GetValue(candidate);
                }
                catch
                {
                    continue;
                }
                if (captured != null && ids.TryGetValue(captured, out var capturedId))
                    Add(capturedId, $"{description} (closure over {FieldName(inner)})");
            }
        }
        return false;
    }

    void InspectItems(IEnumerable enumerable, string description)
    {
        var seen = 0;
        try
        {
            foreach (var item in enumerable)
            {
                if (++seen > MaxCollectionItems)
                    break;
                if (item == null)
                    continue;
                if (item is DictionaryEntry entry)
                {
                    Judge(entry.Key, description, false);
                    Judge(entry.Value, description, false);
                    continue;
                }
                var type = item.GetType();
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    Judge(type.GetProperty("Key")?.GetValue(item), description, false);
                    Judge(type.GetProperty("Value")?.GetValue(item), description, false);
                    continue;
                }
                Judge(item, description, false);
            }
        }
        catch
        {
            // a collection that cannot be enumerated safely off its owner's thread
        }
    }

    void Add(int id, string description)
    {
        if (!found.TryGetValue(id, out var list))
            found[id] = list = [];
        if (!list.Contains(description))
            list.Add(description);
    }

    static bool IsWeakGeneric(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(WeakReference<>) || type.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>));

    /// <summary>"&lt;Foo&gt;k__BackingField" → "Foo"; event backing fields already carry the event's name.</summary>
    static string FieldName(FieldInfo field)
    {
        var name = field.Name;
        return name.StartsWith('<') && name.Contains(">k__BackingField", StringComparison.Ordinal) ? name[1..name.IndexOf('>')] : name;
    }
}
