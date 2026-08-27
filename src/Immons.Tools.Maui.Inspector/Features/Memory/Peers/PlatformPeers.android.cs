using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Peers;

internal static partial class PlatformPeers
{
    static partial void CensusPlatform(ref List<PeerTypeCount>? types, ref int? globalRefs, ref int? weakGlobalRefs)
    {
        var runtime = Java.Interop.JniEnvironment.Runtime;
        var counts = new Dictionary<Type, int>();
        foreach (var info in runtime.ValueManager.GetSurfacedPeers())
        {
            if (info.SurfacedPeer.TryGetTarget(out var peer))
                counts[peer.GetType()] = counts.GetValueOrDefault(peer.GetType()) + 1;
        }

        types = counts
            .Select(kv => new PeerTypeCount(TypeNames.Full(kv.Key), TypeNames.Short(kv.Key), TypeNames.IsApp(kv.Key), kv.Value))
            .OrderByDescending(t => t.App).ThenByDescending(t => t.Count).ThenBy(t => t.Name)
            .ToList();
        globalRefs = runtime.GlobalReferenceCount;
        weakGlobalRefs = runtime.WeakGlobalReferenceCount;
    }
}
