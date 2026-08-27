using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>Where the app runs, as reported by its inspector — what decides how the diagnostic tools reach it.</summary>
internal sealed record DumpTarget(string Platform, bool Virtual, int Pid, string? Diagnostics)
{
    public static DumpTarget From(JsonNode job) => new(
        job["platform"]?.GetValue<string>() ?? "",
        job["virtual"]?.GetValue<bool>() ?? false,
        job["pid"]?.GetValue<int>() ?? 0,
        job["diagnostics"]?.GetValue<string>());

    /// <summary>Mono needs a router; CoreCLR (Windows, desktop) is reached by process id.</summary>
    public bool NeedsRouter => Platform is "android" or "ios";

    /// <summary>The TCP port the app's runtime was built with ("127.0.0.1:9042,connect,nosuspend").</summary>
    public int DiagnosticPort
    {
        get
        {
            var address = Diagnostics?.Split(',')[0] ?? "";
            var colon = address.LastIndexOf(':');
            return colon > 0 && int.TryParse(address[(colon + 1)..], out var port) ? port : 0;
        }
    }

    /// <summary>How a tool addresses this app: the router's socket for Mono, the process id otherwise. Null = no route.</summary>
    public string? Endpoint(DsRouterProcess? router) => NeedsRouter
        ? router == null ? null : $"--diagnostic-port \"{router.Socket}\",connect"
        : Pid > 0 ? $"-p {Pid}" : null;

    /// <summary>
    /// Mono (Android, iOS) has no GCAllocationTick: its own profiler provider reports every
    /// allocation as a vtable id, and the heap-dump keywords at session start name the vtables
    /// (TypeLoading names the ones that appear later). CoreCLR gets the sampled gc-verbose profile.
    /// </summary>
    public const string MonoAllocationProviders = "--providers Microsoft-DotNETRuntimeMonoProfiler:0x8008B00001:4";

    public bool IsMono => NeedsRouter;

    public string Describe() => Platform switch
    {
        "android" => Virtual ? "Android emulator" : "Android device",
        "ios" => Virtual ? "iOS simulator" : "iOS device",
        _ => Platform,
    };
}
