using System.Text.Json.Nodes;
using HeapDumpReaderTests;
using Immons.Tools.Maui.Inspector.Sync.HeapDumps;

// `dotnet run -- <file.nettrace> report` builds the allocation report the panel would show.
if (args.Length == 2 && args[0].EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase) && args[1] == "report" && File.Exists(args[0]))
{
    var allocReport = AllocationRunner.Report(args[0], 10, mono: true, new HashSet<string> { "Immons.Tools.Maui.Inspector.Sample" }, new HashSet<string>(), "test");
    Console.WriteLine($"  events {allocReport["samples"]}, total {allocReport["totalBytes"]} B");
    foreach (var t in ((JsonArray)allocReport["types"]!).Take(15))
        Console.WriteLine($"  {t!["bytes"],10}  {t["samples"],6}  {t["type"]}");
    return 0;
}

// `dotnet run -- <file.nettrace>` counts the events of a trace per provider and name (allocation recording diagnostics).
if (args.Length >= 1 && args[0].EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase) && File.Exists(args[0]))
{
    var counts = new Dictionary<string, int>();
    using (var source = new Microsoft.Diagnostics.Tracing.EventPipeEventSource(args[0]))
    {
        source.Dynamic.All += e => { var key = e.ProviderName + " / " + e.EventName; counts[key] = counts.GetValueOrDefault(key) + 1; };
        source.Process();
    }
    foreach (var (key, count) in counts.OrderByDescending(kv => kv.Value).Take(25))
        Console.WriteLine($"  {count,8}  {key}");
    Console.WriteLine($"  {counts.Values.Sum()} events, {counts.Count} kinds");
    return 0;
}

// `dotnet run -- <file.gcdump> time` measures what building the report costs on that heap.
if (args.Length == 2 && args[1] == "time" && File.Exists(args[0]))
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var loaded = GcDumpReader.Read(args[0]);
    Console.WriteLine($"  read:   {clock.ElapsedMilliseconds,6} ms  ({loaded.MemoryGraph.NodeIndexLimit} objects)");
    clock.Restart();
    var suspects = new[] { "Microsoft.Maui.Controls.Label", "Microsoft.Maui.Controls.Button", "Microsoft.Maui.Controls.Border" };
    var timed = HeapReport.Build(loaded.MemoryGraph, suspects, new HashSet<string>(), new HashSet<string>(), "test", args[0]);
    Console.WriteLine($"  report: {clock.ElapsedMilliseconds,6} ms  ({((JsonArray)timed["types"]!).Count} types, {((JsonArray)timed["roots"]!).Count} traced)");
    foreach (var root in ((JsonArray)timed["roots"]!).OfType<JsonObject>())
        Console.WriteLine($"    {root["matched"]}× {((string?)root["type"] ?? "").Split('.').Last()} · retained {root["retained"]} · hops {(root["paths"] as JsonArray)?.OfType<JsonArray>().FirstOrDefault()?.Count}");
    return 0;
}

// `dotnet run -- <file.gcdump>` only describes an existing dump: who it came from and what it holds.
if (args.Length >= 1 && File.Exists(args[0]))
{
    var existing = GcDumpReader.Read(args[0]);
    var histogram = existing.MemoryGraph.GetHistogramByType();
    Console.WriteLine($"pid {existing.ProcessID} ({existing.ProcessName}) on {existing.MachineName}: {existing.MemoryGraph.NodeIndexLimit} objects, {histogram.Count(h => h.Count > 0)} types");
    if (args.Length == 2)
    {
        var storage = existing.MemoryGraph.AllocTypeNodeStorage();
        var byType = histogram.Where(h => h.Count > 0).Select(h => (existing.MemoryGraph.GetType(h.TypeIdx, storage).Name, h.Count)).Where(t => t.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase) && !t.Name.StartsWith('[')).Take(60);
        foreach (var (name, count) in byType)
            Console.WriteLine($"  {count,6}  {name}");
        return 0;
    }
    var assemblies = new HashSet<string> { "Immons.Tools.Maui.Inspector.Sample" }; // the sample app's assembly name (namespace SampleApp)
    var sampleReport = HeapReport.Build(existing.MemoryGraph, [], assemblies, new HashSet<string>(), "test", args[0]);
    Console.WriteLine("  as the sample's dump: " + (HeapReport.LacksAppTypes(sampleReport, assemblies) ?? "ok — app types present"));
    return 0;
}

// A few objects a static field keeps alive — what the report must find, with a path to a root.
Roots.Held.AddRange(Enumerable.Range(0, 3).Select(_ => new LeakSentinel()));

var target = new DumpTarget("desktop", false, Environment.ProcessId, null);
var tools = new DiagnosticTools();
if (await tools.Check(target, line => { Console.WriteLine("  tools: " + line); return Task.CompletedTask; }) is { } missing)
{
    Console.WriteLine("SKIP: " + missing);
    return 0;
}

var file = Path.Combine(Path.GetTempPath(), $"heapdumpreadertest-{Environment.ProcessId}.gcdump");
var (ok, message) = await GcDumpRunner.Collect(tools.GcDumpPath!, target, null, file, line => { Console.WriteLine("  gcdump: " + line); return Task.CompletedTask; });
if (!ok)
{
    Console.WriteLine("FAIL collect: " + message);
    return 1;
}

var dump = GcDumpReader.Read(file);
Console.WriteLine($"  dump of pid {dump.ProcessID} ({dump.ProcessName}); wrong-process check: {GcDumpReader.WrongProcess(dump, target) ?? "ok"}");
var graph = dump.MemoryGraph;
var report = HeapReport.Build(graph, ["HeapDumpReaderTests.LeakSentinel"], new HashSet<string> { "HeapDumpReaderTests" }, new HashSet<string>(), tools.Describe(), file);
var failures = 0;

var types = (JsonArray)report["types"]!;
Check("has objects", (int)report["totalObjects"]! > 1000);
Check("System.String present", types.Any(t => (string?)t?["type"] == "System.String"));
var sentinel = types.FirstOrDefault(t => ((string?)t?["type"] ?? "").EndsWith("LeakSentinel", StringComparison.Ordinal));
Check("sentinel type listed", sentinel != null);
Check("sentinel count >= 3", sentinel != null && (int)sentinel["count"]! >= 3);
Check("sentinel flagged as app type", sentinel != null && (bool)sentinel["app"]!);
Check("app types sorted first", types.Count > 0 && (bool)types[0]!["app"]!);

var roots = (JsonArray)report["roots"]!;
Check("one root entry", roots.Count == 1);
Check("retained size of the sentinels >= 3 payloads", (long?)roots[0]?["retained"] >= 3 * 4096);
var largest = (JsonArray)report["largest"]!;
Check("largest objects listed", largest.Count > 0 && (long)largest[0]!["bytes"]! >= (long)largest[^1]!["bytes"]!);
Console.WriteLine("  largest: " + string.Join(" | ", largest.Take(3).Select(l => (string?)l!["type"] + " " + l["bytes"] + "B retained " + l["retained"])));
var matched = (int?)roots[0]?["matched"] ?? 0;
Check("root entry matched >= 3", matched >= 3);
var paths = (JsonArray)roots[0]!["paths"]!;
Check("at least one path", paths.Count > 0);
foreach (var path in paths)
    Console.WriteLine("  path: " + string.Join(" <- ", path!.AsArray().Select(s => (string?)s)));
Check("path starts at the sentinel", paths.Count > 0 && ((string?)paths[0]![0]!)!.EndsWith("LeakSentinel", StringComparison.Ordinal));
Check("path ends at a root category", paths.Count > 0 && ((string?)paths[0]!.AsArray().Last()!)!.StartsWith('['));

File.Delete(file);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool condition)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition)
        failures++;
}
