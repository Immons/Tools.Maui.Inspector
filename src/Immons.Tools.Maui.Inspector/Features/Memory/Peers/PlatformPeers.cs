namespace Immons.Tools.Maui.Inspector.Features.Memory.Peers;

/// <summary>
/// The platform's own list of bridged objects. Android's Java.Interop can enumerate every
/// surfaced Java peer — views, handlers' platform views, listeners — including those the tracker
/// never saw. .NET iOS dropped the equivalent (Runtime.GetSurfacedObjects) and Windows has none.
/// </summary>
internal static partial class PlatformPeers
{
    public static PeerCensus Census()
    {
        List<PeerTypeCount>? types = null;
        int? globalRefs = null, weakGlobalRefs = null;
        try
        {
            CensusPlatform(ref types, ref globalRefs, ref weakGlobalRefs);
        }
        catch
        {
            types = null;
        }
        return new PeerCensus(types != null, types ?? [], globalRefs, weakGlobalRefs);
    }

    static partial void CensusPlatform(ref List<PeerTypeCount>? types, ref int? globalRefs, ref int? weakGlobalRefs);
}
