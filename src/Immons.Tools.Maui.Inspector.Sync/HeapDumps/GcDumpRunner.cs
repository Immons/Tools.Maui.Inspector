namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// One `dotnet-gcdump collect`. Against Mono it talks to this app's own dsrouter (started by the
/// tool, one per app), which waits for the runtime to connect — Android through adb reverse, iOS
/// through the simulator's loopback or usbmux — then induces a full GC and streams the heap out.
/// </summary>
internal static class GcDumpRunner
{
    /// <summary>
    /// Collection streams every object out of the app as events, so it scales with the heap: a
    /// million objects is about a minute over dsrouter. The limits are generous on purpose —
    /// a dump that dies at the two-minute mark wastes far more time than one that runs long.
    /// </summary>
    const int CollectTimeoutSeconds = 600;
    static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(12);

    public static Task<(bool Ok, string Message)> Collect(string gcDumpPath, DumpTarget target, DsRouterProcess? router, string outputFile, Func<string, Task> progress)
    {
        if (target.Endpoint(router) is not { } endpoint)
            return Task.FromResult((false, $"heap dumps are not supported on '{target.Platform}'"));
        return ToolRunner.Run(gcDumpPath, $"collect {endpoint} -o \"{outputFile}\" -t {CollectTimeoutSeconds}", outputFile, OverallTimeout, progress);
    }
}
