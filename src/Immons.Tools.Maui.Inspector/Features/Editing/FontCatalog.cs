using System.Collections;
using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Features.Editing;

/// <summary>
/// Font aliases registered with ConfigureFonts. IFontRegistrar exposes no enumeration, so the
/// registry dictionary is read by reflection; an empty list simply means no suggestions.
/// </summary>
internal static class FontCatalog
{
    static readonly string[] FontFileExtensions = [".ttf", ".otf", ".woff", ".woff2"];

    static bool HasFontFileExtension(string value) =>
        FontFileExtensions.Any(e => value.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// (alias, font file) of every font registered with ConfigureFonts — read from the registrar's
    /// (Filename, Alias, Assembly) entries; lookups the app made at runtime are not registrations.
    /// </summary>
    public static IReadOnlyList<(string Alias, string File)> RegisteredFonts()
    {
        try
        {
            var services = MauiInspector.ActiveInspector?.MauiContext?.Services;
            var registrar = services?.GetService(typeof(IFontRegistrar));
            if (registrar == null)
                return [];

            var fonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in registrar.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetValue(registrar) is not IDictionary map)
                    continue;
                foreach (DictionaryEntry entry in map)
                {
                    if (entry.Value is not System.Runtime.CompilerServices.ITuple { Length: >= 2 } registration)
                        continue;
                    var file = Path.GetFileName(registration[0] as string ?? "");
                    var alias = registration[1] as string;
                    if (string.IsNullOrEmpty(alias) && entry.Key is string key && !HasFontFileExtension(key))
                        alias = key;
                    if (!string.IsNullOrEmpty(alias))
                        fonts.TryAdd(alias, file);
                }
            }
            return fonts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => (kv.Key, kv.Value)).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<string> RegisteredAliases()
    {
        try
        {
            var services = MauiInspector.ActiveInspector?.MauiContext?.Services;
            var registrar = services?.GetService(typeof(IFontRegistrar));
            if (registrar == null)
                return [];

            var aliases = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in registrar.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetValue(registrar) is not IDictionary map)
                    continue;
                foreach (var key in map.Keys)
                {
                    // The registry also keys raw file names — only aliases are settable values.
                    if (key is string alias && alias.Length > 0 && !HasFontFileExtension(alias))
                        aliases.Add(alias);
                }
            }
            return aliases.ToList();
        }
        catch
        {
            return [];
        }
    }
}
