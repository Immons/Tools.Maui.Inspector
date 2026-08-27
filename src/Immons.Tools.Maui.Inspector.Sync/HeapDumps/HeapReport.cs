using System.Text.Json.Nodes;
using Graphs;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// The report the panel renders: every app type and the heaviest framework types with object
/// counts and bytes, the largest single objects, and the root paths (with retained sizes) of the
/// snapshot's suspects.
/// </summary>
internal static class HeapReport
{
    const int FrameworkTypesKept = 300;
    const int SuspectTypesTraced = 20;
    const int LargestKept = 30;

    public static JsonObject Build(MemoryGraph graph, IReadOnlyList<string> suspectTypes, ISet<string> appAssemblies, ISet<string> packageAssemblies, string tool, string file)
    {
        var typeStorage = graph.AllocTypeNodeStorage();
        var entries = new List<(string Type, string Module, bool App, bool Package, int Count, long Bytes)>();
        var inspectorTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in graph.GetHistogramByType())
        {
            if (entry.Count == 0)
                continue;
            var type = graph.GetType(entry.TypeIdx, typeStorage);
            var module = ModuleName(type.ModuleName);
            var name = type.Name ?? "";
            entries.Add((name, module, IsFrom(module, name, appAssemblies), IsFrom(module, name, packageAssemblies), entry.Count, entry.Size));
            if (IsInspector(module, name))
                inspectorTypes.Add(name);
        }

        var types = new JsonArray();
        var frameworkKept = 0;
        foreach (var entry in entries.OrderByDescending(e => e.App).ThenByDescending(e => e.Bytes))
        {
            if (!entry.App && frameworkKept++ >= FrameworkTypesKept)
                continue;
            types.Add(new JsonObject
            {
                ["type"] = entry.Type,
                ["module"] = entry.Module,
                ["app"] = entry.App,
                ["package"] = entry.Package,
                ["inspector"] = inspectorTypes.Contains(entry.Type),
                ["count"] = entry.Count,
                ["bytes"] = entry.Bytes,
            });
        }

        var rootPaths = new RootPaths(graph);
        return new JsonObject
        {
            ["kind"] = "dump",
            ["tool"] = tool,
            ["file"] = file,
            ["collectedAt"] = DateTime.Now.ToString("HH:mm:ss"),
            ["totalObjects"] = (int)graph.NodeIndexLimit,
            ["totalBytes"] = graph.TotalSize,
            ["typeCount"] = entries.Count,
            ["types"] = types,
            ["roots"] = Roots(rootPaths, suspectTypes, appAssemblies, packageAssemblies),
            ["largest"] = Largest(graph, rootPaths, appAssemblies),
            ["inspectorBytes"] = entries.Where(e => inspectorTypes.Contains(e.Type)).Sum(e => e.Bytes),
        };
    }

    /// <summary>A trace job: the root paths of one more type, from a dump already on disk.</summary>
    public static JsonObject BuildTrace(MemoryGraph graph, string type, int sourceJobId, string tool, string file) => new()
    {
        ["kind"] = "trace",
        ["tool"] = tool,
        ["file"] = file,
        ["sourceJob"] = sourceJobId,
        ["roots"] = Roots(new RootPaths(graph), [type], new HashSet<string>(), new HashSet<string>()),
    };

    /// <summary>
    /// Each traced type with its chains — and who owns the type, so the panel can filter and colour
    /// it without guessing from a types list that may have been truncated.
    /// </summary>
    public static JsonArray Roots(RootPaths rootPaths, IReadOnlyList<string> suspectTypes, ISet<string> appAssemblies, ISet<string> packageAssemblies)
    {
        var roots = new JsonArray();
        foreach (var suspect in suspectTypes.Distinct().Take(SuspectTypesTraced))
        {
            var (matched, retained, paths) = rootPaths.For(suspect);
            var pathsJson = new JsonArray();
            foreach (var path in paths)
                pathsJson.Add(new JsonArray(path.Select(step => (JsonNode)step).ToArray()));
            roots.Add(new JsonObject
            {
                ["type"] = suspect,
                ["app"] = IsFrom("", suspect, appAssemblies),
                ["package"] = IsFrom("", suspect, packageAssemblies),
                ["inspector"] = IsInspector("", suspect),
                ["matched"] = matched,
                ["retained"] = retained,
                ["paths"] = pathsJson,
            });
        }
        return roots;
    }

    /// <summary>The biggest single objects — arrays behind images, caches, buffers — each with its chain to a root.</summary>
    static JsonArray Largest(MemoryGraph graph, RootPaths rootPaths, ISet<string> appAssemblies)
    {
        var node = graph.AllocNodeStorage();
        var top = new List<(NodeIndex Index, int Size)>();
        for (NodeIndex index = 0; index < graph.NodeIndexLimit; index++)
        {
            var size = graph.GetNode(index, node).Size;
            if (top.Count < LargestKept)
            {
                top.Add((index, size));
                if (top.Count == LargestKept)
                    top.Sort((a, b) => a.Size.CompareTo(b.Size));
                continue;
            }
            if (size <= top[0].Size)
                continue;
            top[0] = (index, size);
            top.Sort((a, b) => a.Size.CompareTo(b.Size));
        }

        var largest = new JsonArray();
        foreach (var (index, size) in top.OrderByDescending(t => t.Size))
        {
            var path = rootPaths.Walk(index);
            largest.Add(new JsonObject
            {
                ["type"] = path[0],
                ["bytes"] = (long)size,
                ["retained"] = rootPaths.RetainedOf(index),
                ["app"] = appAssemblies.Any(a => path[0].StartsWith(a + ".", StringComparison.Ordinal)),
                // A buffer the inspector holds is the inspector's, whatever its own type says.
                ["inspector"] = path.Any(step => IsInspector("", step)),
                ["path"] = new JsonArray(path.Select(step => (JsonNode)step).ToArray()),
            });
        }
        return largest;
    }

    /// <summary>
    /// The second wrong-process check, for dumps that carry no process id: a heap without a single
    /// type from the app's own assemblies is not the app's heap — another diagnostics-enabled app
    /// on the same host answered on the diagnostic port.
    /// </summary>
    public static string? LacksAppTypes(JsonObject report, ISet<string> appAssemblies)
    {
        if (appAssemblies.Count == 0 || ((JsonArray)report["types"]!).Any(t => t?["app"]?.GetValue<bool>() == true))
            return null;
        return $"the dump holds no type from the app's assemblies ({string.Join(", ", appAssemblies.Take(3))}…) — it describes another process: "
            + "another app with the diagnostic port is running on the same host and owns port 9000; stop it and dump again";
    }

    /// <summary>
    /// The inspector's own objects. A memory tool that fills the memory view with its own trackers,
    /// reports and screenshot buffers is noise; the panel hides these unless asked.
    /// </summary>
    const string InspectorPrefix = "Immons.Tools.Maui.Inspector";

    static bool IsInspector(string module, string typeName) =>
        module.StartsWith(InspectorPrefix, StringComparison.Ordinal)
        || typeName.StartsWith(InspectorPrefix + ".", StringComparison.Ordinal);

    /// <summary>Module names arrive as file names or paths, sometimes empty on Mono.</summary>
    static string ModuleName(string? module) =>
        string.IsNullOrEmpty(module) ? "" : Path.GetFileNameWithoutExtension(module);

    /// <summary>By module when the dump has one; else the namespace has to name one of the assemblies.</summary>
    static bool IsFrom(string module, string typeName, ISet<string> assemblies) =>
        module.Length > 0
            ? assemblies.Contains(module)
            : assemblies.Any(assembly => typeName.StartsWith(assembly + ".", StringComparison.Ordinal));
}
