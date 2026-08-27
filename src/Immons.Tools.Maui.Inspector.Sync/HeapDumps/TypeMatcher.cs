using Graphs;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// Finds the dump's type entries for a runtime type name. The app reports Type.FullName
/// (nested as Outer+Inner, generics with a backtick); the dump names types the runtime's own way,
/// so the match relaxes step by step: exact, then without generic/array decorations, then by the
/// simple name at the end of a namespace.
/// </summary>
internal static class TypeMatcher
{
    public static HashSet<NodeTypeIndex> Match(MemoryGraph graph, string typeName)
    {
        var storage = graph.AllocTypeNodeStorage();
        var names = new Dictionary<NodeTypeIndex, string>();
        for (NodeTypeIndex index = 0; index < graph.NodeTypeIndexLimit; index++)
            names[index] = graph.GetType(index, storage).Name ?? "";

        var exact = names.Where(kv => kv.Value == typeName).Select(kv => kv.Key).ToHashSet();
        if (exact.Count > 0)
            return exact;

        var wanted = Bare(typeName);
        var bare = names.Where(kv => Bare(kv.Value) == wanted).Select(kv => kv.Key).ToHashSet();
        if (bare.Count > 0)
            return bare;

        var simple = wanted[(wanted.LastIndexOfAny(['.', '+']) + 1)..];
        return names.Where(kv => Bare(kv.Value) is var bareName
                && (bareName == simple || bareName.EndsWith("." + simple, StringComparison.Ordinal) || bareName.EndsWith("+" + simple, StringComparison.Ordinal)))
            .Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>
    /// The name without generic arguments, array ranks, pointer marks and PerfView's size bucket —
    /// objects over 1 KB are typed "Foo (Bytes > 1K)" in a dump, which no runtime name carries.
    /// </summary>
    static string Bare(string name)
    {
        var bucket = name.IndexOf(" (Bytes > ", StringComparison.Ordinal);
        if (bucket > 0)
            name = name[..bucket];
        var cut = name.IndexOfAny(['`', '[', '<']);
        return cut < 0 ? name : name[..cut];
    }
}
