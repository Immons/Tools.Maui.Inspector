using Graphs;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// "Who holds it" and "how much it costs". The chain is found by walking the reference graph
/// backwards, breadth-first, from the object to the nearest GC root: the shortest chain is the one
/// worth reading — a spanning tree gives one path per object, but not the shortest, and a leak
/// explained in seven hops beats the same leak explained in two hundred. The retained size (what
/// would go away with the object) still comes from the spanning tree, which is what defines it.
/// The gcdump format carries no field names, so types are all there is.
/// </summary>
internal sealed class RootPaths
{
    const int MaxPathsPerType = 3;
    const int MaxVisited = 400_000;

    readonly MemoryGraph _graph;
    readonly SpanningTree _tree;
    readonly RefGraph _refs;
    readonly Node _node;
    readonly NodeType _type;
    readonly long[] _retained;

    public RootPaths(MemoryGraph graph)
    {
        _graph = graph;
        _node = graph.AllocNodeStorage();
        _type = graph.AllocTypeNodeStorage();
        _tree = new SpanningTree(graph, TextWriter.Null);
        _refs = new RefGraph(graph);
        var order = new List<NodeIndex>((int)graph.NodeIndexLimit);
        _tree.ForEach(order.Add); // assigns the parents, parents before children
        _retained = RetainedSizes(order);
    }

    public long RetainedOf(NodeIndex node) => _retained[(int)node];

    public (int Matched, long Retained, List<List<string>> Paths) For(string typeName)
    {
        var types = TypeMatcher.Match(_graph, typeName);
        var matched = 0;
        var retained = 0L;
        var paths = new List<List<string>>();
        var seen = new HashSet<string>();

        for (NodeIndex index = 0; index < _graph.NodeIndexLimit; index++)
        {
            if (!types.Contains(_graph.GetNode(index, _node).TypeIndex))
                continue;
            matched++;
            retained += _retained[(int)index];
            if (paths.Count >= MaxPathsPerType)
                continue;
            var path = Walk(index);
            if (seen.Add(string.Join("|", path)))
                paths.Add(path);
        }
        return (matched, retained, paths);
    }

    /// <summary>The shortest chain from this object to a root, as type names, the object first.</summary>
    public List<string> Walk(NodeIndex start)
    {
        var cameFrom = new Dictionary<NodeIndex, NodeIndex> { [start] = NodeIndex.Invalid };
        var queue = new Queue<NodeIndex>();
        queue.Enqueue(start);
        var refStorage = _refs.AllocNodeStorage();

        while (queue.Count > 0 && cameFrom.Count < MaxVisited)
        {
            var current = queue.Dequeue();
            var referrers = _refs.GetNode(current, refStorage);
            for (var referrer = referrers.GetFirstChildIndex(); referrer != NodeIndex.Invalid; referrer = referrers.GetNextChildIndex())
            {
                if (!cameFrom.TryAdd(referrer, current))
                    continue;
                if (referrer == _graph.RootIndex)
                    return Rebuild(cameFrom, referrer, start);
                queue.Enqueue(referrer);
            }
        }

        // Nothing referred to it, or the search gave up: fall back to the spanning tree's parents.
        return WalkSpanningTree(start);
    }

    /// <summary>
    /// The search ran from the object outwards, so cameFrom[x] is the node x was reached from —
    /// one step closer to the object. Following it from the root and reversing gives the chain the
    /// way it reads: the leaked object first, its holder next, the root last.
    /// </summary>
    List<string> Rebuild(Dictionary<NodeIndex, NodeIndex> cameFrom, NodeIndex root, NodeIndex start)
    {
        var chain = new List<NodeIndex>();
        var node = root;
        while (true)
        {
            if (node != _graph.RootIndex)
                chain.Add(node);
            if (node == start || !cameFrom.TryGetValue(node, out var next) || next == NodeIndex.Invalid)
                break;
            node = next;
        }
        chain.Reverse();
        return chain.Select(TypeName).ToList();
    }

    List<string> WalkSpanningTree(NodeIndex start)
    {
        var path = new List<string>();
        var current = start;
        for (var depth = 0; depth < 40; depth++)
        {
            path.Add(TypeName(current));
            var parent = _tree.Parent(current);
            if (parent == NodeIndex.Invalid)
            {
                path.Add("[unreachable — not collected yet]");
                break;
            }
            if (parent == _graph.RootIndex)
                break;
            current = parent;
        }
        return path;
    }

    public string TypeName(NodeIndex index) => _graph.GetType(_graph.GetNode(index, _node).TypeIndex, _type).Name;

    /// <summary>Children were visited after their parents, so folding the list backwards sums every subtree once.</summary>
    long[] RetainedSizes(List<NodeIndex> order)
    {
        var retained = new long[(int)_graph.NodeIndexLimit + 1];
        for (NodeIndex index = 0; index < _graph.NodeIndexLimit; index++)
            retained[(int)index] = _graph.GetNode(index, _node).Size;
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var parent = _tree.Parent(order[i]);
            if (parent != NodeIndex.Invalid && parent != _graph.RootIndex)
                retained[(int)parent] += retained[(int)order[i]];
        }
        return retained;
    }
}
