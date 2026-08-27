using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Shared;

/// <summary>
/// Who wrote what. Three kinds, because "not the framework" is not the same as "yours": the app's
/// **own** assemblies (the ones you can edit), the **packages** it pulled in (CommunityToolkit,
/// SQLite-net, Mapster — third-party, but just as absent from the framework list), and the
/// framework itself. Ownership is decided by the root of the app assembly's name — the app class
/// lives in it — so Contoso.Shop.Mobile owns Contoso.Shop.Model and nothing else.
/// </summary>
internal static class AppAssemblies
{
    /// <summary>Everything loaded that is neither the framework nor the inspector — own code and packages.</summary>
    public static IEnumerable<Assembly> Own() =>
        AppDomain.CurrentDomain.GetAssemblies().Where(a => !IsFramework(a.GetName().Name ?? ""));

    /// <summary>Only the app's own assemblies.</summary>
    public static IEnumerable<Assembly> OwnOnly() =>
        Own().Where(a => IsOwn(a.GetName().Name ?? ""));

    public static bool IsFramework(string name) =>
        name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Mono.", StringComparison.Ordinal)
        || name.StartsWith("Java.", StringComparison.Ordinal)
        || name.StartsWith("Xamarin.", StringComparison.Ordinal)
        || name.StartsWith("SkiaSharp", StringComparison.Ordinal)
        || name is "mscorlib" or "netstandard" or "WindowsBase"
        || name is "Immons.Tools.Maui.Inspector" or "Immons.Tools.Maui.Inspector.Persistency" or "Immons.Tools.Maui.Inspector.Extensions";

    /// <summary>The app's own code: the assembly the App class lives in and its siblings under the same root name.</summary>
    public static bool IsOwn(string name)
    {
        if (name.Length == 0 || IsFramework(name))
            return false;
        if (MauiInspector.Options.Memory.AppAssemblyPrefixes.Count > 0)
            return MauiInspector.Options.Memory.AppAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return Root is { Length: > 0 } root
            && (name.Equals(EntryName, StringComparison.Ordinal) || name.StartsWith(root + ".", StringComparison.Ordinal));
    }

    static string? _entryName;

    /// <summary>The app's own assembly: where the Application subclass is, with the entry assembly as the fallback.</summary>
    static string EntryName => _entryName ??=
        (Application.Current?.GetType().Assembly ?? Assembly.GetEntryAssembly())?.GetName().Name ?? "";

    static string Root => EntryName.Split('.')[0];
}
