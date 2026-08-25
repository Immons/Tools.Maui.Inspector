using System.Reflection;

namespace Immons.Tools.Maui.Inspector.Shared;

/// <summary>The app's own assemblies — everything loaded that is neither the framework nor the inspector.</summary>
internal static class AppAssemblies
{
    public static IEnumerable<Assembly> Own() =>
        AppDomain.CurrentDomain.GetAssemblies().Where(a => !IsFramework(a.GetName().Name ?? ""));

    public static bool IsFramework(string name) =>
        name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Mono.", StringComparison.Ordinal)
        || name.StartsWith("Java.", StringComparison.Ordinal)
        || name.StartsWith("Xamarin.", StringComparison.Ordinal)
        || name.StartsWith("SkiaSharp", StringComparison.Ordinal)
        || name is "mscorlib" or "netstandard" or "WindowsBase"
        || name is "Immons.Tools.Maui.Inspector" or "Immons.Tools.Maui.Inspector.Persistency" or "Immons.Tools.Maui.Inspector.Extensions";
}
