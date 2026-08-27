using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>
/// What the live screens still reach. A view model that no element is bound to right now is not
/// necessarily garbage: apps park state between screens — a filter built on one page and read by
/// the popup that opens next. If a view model of an attached element still references it, it is in
/// use, not leaked. Bounded on purpose: a few levels down from the live view models, a fixed visit
/// budget, and never through weak references.
/// </summary>
internal sealed class LiveReachability
{
    const int MaxDepth = 3;
    const int MaxVisited = 20_000;
    const int MaxItemsPerCollection = 500;
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    readonly HashSet<object> _reached = new(ReferenceEqualityComparer.Instance);
    int _visited;

    /// <param name="liveContexts">View models of elements that are on screen.</param>
    /// <param name="liveElements">
    /// The elements themselves. Only fields declared by the app's own classes are read — a page
    /// that parks state in a field is the same case as a view model that does, while MAUI's own
    /// internals (children, bindable values) would drag in the whole visual tree for nothing.
    /// </param>
    public static LiveReachability From(IEnumerable<object> liveContexts, IEnumerable<Element> liveElements)
    {
        var reachability = new LiveReachability();
        foreach (var context in liveContexts)
            reachability.Walk(context, 0);
        foreach (var element in liveElements)
            reachability.WalkOwnFields(element);
        return reachability;
    }

    /// <summary>Reads the object's own declared fields, skipping the framework's part of the hierarchy.</summary>
    void WalkOwnFields(Element element)
    {
        for (var type = element.GetType(); type != null && TypeNames.IsApp(type); type = type.BaseType)
        {
            foreach (var field in SafeFields(type))
            {
                try
                {
                    Walk(field.GetValue(element), 1);
                }
                catch
                {
                    // unreadable field; the rest still counts
                }
            }
        }
    }

    public bool Reaches(object candidate) => _reached.Contains(candidate);

    void Walk(object? value, int depth)
    {
        if (value == null || depth > MaxDepth || _visited >= MaxVisited)
            return;
        if (value is string or Delegate or WeakReference || IsWeakGeneric(value.GetType()) || value.GetType().IsValueType)
            return;
        // An element the app parked is a leak, not shared state — following the visual tree here
        // would also make every live page's whole subtree look "in use" from the wrong direction.
        if (value is Element or IElementHandler)
            return;
        if (!_reached.Add(value))
            return;
        _visited++;

        if (value is IEnumerable items and not IDictionary)
        {
            WalkItems(items, depth);
            return;
        }
        if (value is IDictionary map)
        {
            WalkItems(map.Values, depth);
            return;
        }

        for (var type = value.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in SafeFields(type))
            {
                try
                {
                    Walk(field.GetValue(value), depth + 1);
                }
                catch
                {
                    // a field this runtime will not read; the rest of the walk still counts
                }
            }
        }
    }

    void WalkItems(IEnumerable items, int depth)
    {
        var seen = 0;
        try
        {
            foreach (var item in items)
            {
                if (++seen > MaxItemsPerCollection || _visited >= MaxVisited)
                    return;
                Walk(item, depth + 1);
            }
        }
        catch
        {
            // a collection that cannot be enumerated off its owner's thread
        }
    }

    static IEnumerable<FieldInfo> SafeFields(Type type)
    {
        try
        {
            return type.GetFields(Fields).Where(f => !f.FieldType.IsValueType && !f.IsLiteral);
        }
        catch
        {
            return [];
        }
    }

    static bool IsWeakGeneric(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(WeakReference<>) || type.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>));
}
