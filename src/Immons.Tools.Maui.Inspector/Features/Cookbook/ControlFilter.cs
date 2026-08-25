using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Maui.Controls.Xaml;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook;

/// <summary>
/// Which controls the cookbook renders: the include list (when given) admits, the exclude list
/// vetoes. Entries are prefixes matched against the type's full name ("MyApp.Controls.New.")
/// and against the folder of its XAML file ("Views/New/") — moving old controls to a namespace
/// or a folder of their own is all it takes to keep them out.
/// </summary>
internal sealed class ControlFilter(CookbookOptions options)
{
    static readonly ConcurrentDictionary<Assembly, Dictionary<Type, string>> XamlPaths = new();

    public bool Allows(Type type)
    {
        var keys = KeysOf(type);
        if (options.IncludedControls.Count > 0 && !options.IncludedControls.Any(prefix => Matches(keys, prefix)))
            return false;
        return !options.ExcludedControls.Any(prefix => Matches(keys, prefix));
    }

    static bool Matches(IReadOnlyList<string> keys, string prefix)
    {
        var normalized = Normalize(prefix);
        return normalized.Length > 0 && keys.Any(key => key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    static IReadOnlyList<string> KeysOf(Type type)
    {
        var keys = new List<string>(2) { type.FullName ?? type.Name };
        if (XamlPathOf(type) is { } path)
            keys.Add(path);
        return keys;
    }

    /// <summary>"Views/New/Badge.xaml" for XAML-defined controls — the path the compiler recorded.</summary>
    static string? XamlPathOf(Type type)
    {
        var paths = XamlPaths.GetOrAdd(type.Assembly, static assembly =>
        {
            var map = new Dictionary<Type, string>();
            try
            {
                foreach (var attribute in assembly.GetCustomAttributes<XamlResourceIdAttribute>())
                {
                    if (attribute.Type != null && attribute.Path != null)
                        map.TryAdd(attribute.Type, Normalize(attribute.Path));
                }
            }
            catch
            {
                // reflection-hostile assembly — type names still filter
            }
            return map;
        });
        return paths.GetValueOrDefault(type);
    }

    static string Normalize(string value) => value.Trim().Replace('\\', '/').TrimStart('/');
}
