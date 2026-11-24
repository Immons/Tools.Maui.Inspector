using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Sync;

namespace Immons.Tools.Maui.Inspector.Web.Endpoints;

/// <summary>
/// GET /api/discover — this app sweeps the /24 subnet(s) of its own Wi-Fi / Ethernet on ports
/// 9295–9299 and lists the other inspectors that answer (name, model, URL). The panel's device
/// picker and Devices tab use it for physical phones: a browser cannot do the sweep itself —
/// ~1 300 fetches to silent addresses exhaust its socket pool for many seconds. Shares the
/// discovery code with maui-inspector-sync. On a physical iPhone the first sweep may raise iOS's
/// Local Network prompt (it is an outgoing connection to the LAN).
/// </summary>
internal sealed class DiscoverEndpoint : IHttpEndpoint
{
    static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false })
    {
        // Managed handler on purpose: the platform ones apply ATS / the cleartext policy, which
        // block plain HTTP to LAN addresses in most apps.
        Timeout = TimeSpan.FromSeconds(3),
    };

    static readonly SemaphoreSlim OneSweep = new(1, 1);

    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Get || path != ApiRoutes.Discover.Lan)
            return false;

        var watch = Stopwatch.StartNew();
        var hosts = InspectorDiscovery.LocalSubnetHosts();
        var found = new JsonArray();
        if (hosts.Count > 0)
        {
            await OneSweep.WaitAsync().ConfigureAwait(false);
            try
            {
                var discovery = new InspectorDiscovery(Client);
                foreach (var hit in await discovery.ScanLan(hosts, InspectorDiscovery.LanPorts, TimeSpan.FromMilliseconds(400)).ConfigureAwait(false))
                {
                    if (hit.Instance == RemoteServer.InstanceId)
                        continue;
                    found.Add(new JsonObject { ["url"] = hit.Url, ["label"] = hit.Label, ["instance"] = hit.Instance });
                }
            }
            finally
            {
                OneSweep.Release();
            }
        }

        var json = new JsonObject
        {
            ["found"] = found,
            ["hosts"] = hosts.Count,
            ["ports"] = new JsonArray(InspectorDiscovery.LanPorts.Select(p => (JsonNode)p).ToArray()),
            ["ms"] = watch.ElapsedMilliseconds,
        };
        await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
        return true;
    }
}
