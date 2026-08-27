namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

/// <summary>
/// Whether dotnet-gcdump can reach this process. CoreCLR (Windows) always has its default
/// diagnostic port; Mono on Android/iOS only has the one the build configured through
/// DOTNET_DiagnosticPorts — what the Immons.Tools.Maui.Inspector.Diagnostics package sets up.
/// </summary>
internal static class DiagnosticsPort
{
    const string Variable = "DOTNET_DiagnosticPorts";

    public static string? Configured =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value ? value : null;

    public static bool BuiltIn => OperatingSystem.IsWindows();

    public static bool Available => BuiltIn || Configured != null;

    /// <summary>
    /// Whether allocation recording can work: CoreCLR samples allocations by itself; Mono only reports
    /// them when started with MONO_DIAGNOSTICS=--diagnostic-mono-profiler=alloc (the Diagnostics
    /// package's MauiInspectorAllocationTracking switch).
    /// </summary>
    public static bool AllocationTracking =>
        BuiltIn || (Environment.GetEnvironmentVariable("MONO_DIAGNOSTICS") ?? "").Contains("alloc", StringComparison.Ordinal);
}
