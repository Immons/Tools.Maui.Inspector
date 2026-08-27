namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>Type naming shared by the snapshot rows, the peer census and the heap-dump matching.</summary>
internal static class TypeNames
{
    public static string Full(Type type) => type.FullName ?? type.Name;

    public static string Short(Type type) => type.Name;

    /// <summary>The last segment of a full name — nested types keep the '+' part.</summary>
    public static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];

    /// <summary>The app's own types — its own assemblies, not the packages it uses and not the framework.</summary>
    public static bool IsApp(Type type) => AppAssemblies.IsOwn(type.Assembly.GetName().Name ?? "");

    /// <summary>A third-party package's type: not the framework, but not the app's code either.</summary>
    public static bool IsPackage(Type type) =>
        type.Assembly.GetName().Name is { } name && !AppAssemblies.IsFramework(name) && !AppAssemblies.IsOwn(name);
}
