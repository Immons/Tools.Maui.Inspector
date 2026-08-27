using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

/// <summary>What a heap dump found for one type: the shortest chains to a GC root and what that root is.</summary>
internal sealed record TypeChains(int JobId, int Matched, long Retained, string RootKind, IReadOnlyList<IReadOnlyList<string>> Chains);

/// <summary>
/// The newest heap dump, read back by type. The scan inside the process finds static fields and
/// the events of long-lived objects; everything else — a native peer holding a page, an object
/// buried in a framework cache — only a dump can explain, and this is how that answer reaches the
/// API instead of only the panel. Read once per snapshot: the report is a megabyte of JSON and a
/// snapshot has hundreds of suspects to answer for.
/// </summary>
internal static class DumpChains
{
    public static IReadOnlyDictionary<string, TypeChains> Latest(IHeapDumpRequests requests)
    {
        foreach (var job in requests.Jobs)
        {
            if (job.Phase != HeapDumpPhase.Done || job.ReportJson is not { } json)
                continue;
            JsonNode? report;
            try
            {
                report = JsonNode.Parse(json);
            }
            catch
            {
                continue;
            }
            if (report?["roots"] is not JsonArray roots)
                continue;

            var byType = new Dictionary<string, TypeChains>(StringComparer.Ordinal);
            foreach (var root in roots.OfType<JsonObject>())
            {
                if ((string?)root["type"] is not { Length: > 0 } type)
                    continue;
                var chains = (root["paths"] as JsonArray)?
                    .OfType<JsonArray>()
                    .Select(path => (IReadOnlyList<string>)path.Select(step => (string?)step ?? "").ToList())
                    .ToList() ?? [];
                byType[type] = new TypeChains(job.Id, root["matched"]?.GetValue<int>() ?? 0,
                    root["retained"]?.GetValue<long>() ?? 0, RootKind(chains), chains);
            }
            return byType;
        }
        return new Dictionary<string, TypeChains>();
    }

    /// <summary>
    /// Where the chain ends, in one word — the difference between "unsubscribe this event" and
    /// "the native object was never released, and managed code cannot help you".
    /// </summary>
    static string RootKind(IReadOnlyList<IReadOnlyList<string>> chains)
    {
        var last = chains.Count > 0 && chains[0].Count > 0 ? chains[0][^1] : "";
        if (last.Length == 0 || !last.StartsWith('['))
            return "";
        var lower = last.ToLowerInvariant();
        return lower.Contains("strong handle") ? "interop"
            : lower.StartsWith("[static") ? "static"
            : lower.Contains("handle") ? "handle"
            : lower.Contains("local vars") ? "local"
            : "root";
    }
}
