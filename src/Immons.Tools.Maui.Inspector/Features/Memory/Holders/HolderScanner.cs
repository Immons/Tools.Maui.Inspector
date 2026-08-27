using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Holders;

/// <summary>
/// The in-process "who holds it" for the usual suspects: the static fields of the app's own types
/// (events, plain references, collections one level deep) and the events and fields of the
/// long-lived objects every MAUI app has — Application, its windows, the Shell and page
/// containers. Reflection only, read-only, bounded; reading a static field can run a type's
/// static constructor, which is why this runs only during a snapshot. What it cannot see, the
/// heap dump can.
/// </summary>
internal sealed class HolderScanner(IServiceLifetimes lifetimes) : IHolderScanner
{
    const int MaxTypes = 8000;
    const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    const BindingFlags Instances = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public Dictionary<int, List<string>> Scan(IReadOnlyList<(int Id, object Target)> suspects)
    {
        var found = new Dictionary<int, List<string>>();
        if (suspects.Count == 0)
            return found;
        var ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var (id, target) in suspects)
            ids.TryAdd(target, id);
        var sink = new HolderSink(ids, found);

        var scanned = 0;
        foreach (var type in AppTypes())
        {
            if (++scanned > MaxTypes)
                break;
            foreach (var field in Fields(type, Statics))
                sink.Inspect(field, null, "static ", TypeNames.Short(type));
        }

        foreach (var root in LongLivedRoots())
        {
            var owner = (lifetimes.IsSingleton(root.GetType()) ? "singleton " : "") + (root is Element element ? ParentChain.Label(element) : TypeNames.Short(root.GetType()));
            for (var type = root.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in Fields(type, Instances))
                    sink.Inspect(field, root, "", owner);
            }
        }
        return found;
    }

    static IEnumerable<Type> AppTypes()
    {
        foreach (var assembly in AppAssemblies.Own())
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }
            foreach (var type in types)
            {
                if (type is { IsGenericTypeDefinition: false, IsEnum: false })
                    yield return type;
            }
        }
    }

    static IEnumerable<FieldInfo> Fields(Type type, BindingFlags flags)
    {
        try
        {
            return type.GetFields(flags).Where(f => !f.FieldType.IsValueType && !f.IsLiteral);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Application, its windows and their page containers, the Shell — the objects that outlive every page.</summary>
    static IEnumerable<object> LongLivedRoots()
    {
        var roots = new List<object>();
        if (Application.Current is { } app)
        {
            roots.Add(app);
            foreach (var window in app.Windows)
            {
                roots.Add(window);
                if (window.Page is { } page)
                    roots.Add(page);
            }
        }
        if (Shell.Current is { } shell && !roots.Contains(shell))
            roots.Add(shell);
        return roots;
    }
}
