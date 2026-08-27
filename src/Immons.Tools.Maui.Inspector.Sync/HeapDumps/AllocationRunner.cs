using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// Allocation recording: `dotnet-trace collect` for a few seconds, then the allocation events of
/// the .nettrace summed per type. CoreCLR samples one GCAllocationTick per ~100 KB handed out;
/// Mono's profiler provider reports every allocation as a vtable id, named through the heap-dump
/// vtable references at session start and the type-loading events after it.
/// </summary>
internal static class AllocationRunner
{
    const int TypesKept = 300;

    public static Task<(bool Ok, string Message)> Record(string tracePath, DumpTarget target, DsRouterProcess? router, int seconds, string outputFile, Func<string, Task> progress)
    {
        if (target.Endpoint(router) is not { } endpoint)
            return Task.FromResult((false, $"allocation recording is not supported on '{target.Platform}'"));
        var providers = target.IsMono ? DumpTarget.MonoAllocationProviders : "--profile gc-verbose";
        var duration = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
        return ToolRunner.Run(tracePath, $"collect {endpoint} {providers} --duration {duration} -o \"{outputFile}\"", outputFile, TimeSpan.FromSeconds(seconds + 150), progress);
    }

    public static JsonObject Report(string file, int seconds, bool mono, ISet<string> appAssemblies, ISet<string> packageAssemblies, string tool)
    {
        var tally = new AllocationTally();
        using (var source = new EventPipeEventSource(file))
        {
            source.Clr.GCAllocationTick += (GCAllocationTickTraceData data) => tally.Sampled(data.TypeName, data.AllocationAmount64);
            source.Dynamic.All += tally.MonoEvent;
            source.Process();
        }

        var types = new JsonArray();
        foreach (var (type, bytes, count) in tally.ByType().Take(TypesKept))
        {
            types.Add(new JsonObject
            {
                ["type"] = type,
                ["bytes"] = bytes,
                ["samples"] = count,
                ["app"] = appAssemblies.Any(a => type.StartsWith(a + ".", StringComparison.Ordinal)),
                ["package"] = packageAssemblies.Any(a => type.StartsWith(a + ".", StringComparison.Ordinal)),
            });
        }
        return new JsonObject
        {
            ["kind"] = "alloc",
            ["tool"] = tool,
            ["file"] = file,
            ["seconds"] = seconds,
            ["sampled"] = !mono,
            ["samples"] = tally.Events,
            ["totalBytes"] = tally.TotalBytes,
            ["types"] = types,
        };
    }
}
