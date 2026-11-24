using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>GET /api/network — requests captured by <see cref="MauiInspectorHttpHandler"/>.</summary>
internal sealed class NetworkEndpoint(INetworkLog network) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method == HttpVerbs.Post && path == ApiRoutes.Network.Clear)
        {
            network.Clear();
            await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Get)
            return false;

        if (path == ApiRoutes.Network.List)
        {
            await HttpResponse.WriteJson(context, network.ToJson()).ConfigureAwait(false);
            return true;
        }

        if (path == ApiRoutes.Network.Body)
        {
            long.TryParse(context.Request.QueryString["seq"], out var seq);
            if (network.BodiesJson(seq) is { } json)
                await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
            else
                await HttpResponse.WriteText(context, 404, "entry not found").ConfigureAwait(false);
            return true;
        }

        return false;
    }
}
