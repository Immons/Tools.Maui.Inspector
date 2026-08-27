using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Web;

/// <summary>
/// POST /api/memory/snapshot runs a leak snapshot, GET returns the latest one, and
/// POST /api/memory/baseline marks the current state as the one everything is measured against.
/// </summary>
internal sealed class SnapshotEndpoint(ISnapshotRunner snapshots, IHeapDumpRequests dumps) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Post && path == ApiRoutes.Memory.Baseline)
        {
            var clear = (await RequestBody.ReadJson(context).ConfigureAwait(false))?["clear"]?.GetValue<bool>() ?? false;
            await snapshots.MarkBaselineAsync(clear).ConfigureAwait(false);
            await Respond(context).ConfigureAwait(false);
            return true;
        }

        if (path != ApiRoutes.Memory.Snapshot || method is not (HttpVerbs.Get or HttpVerbs.Post))
            return false;

        if (method == HttpVerbs.Post)
            await snapshots.RunAsync().ConfigureAwait(false);
        await Respond(context).ConfigureAwait(false);
        return true;
    }

    Task Respond(HttpListenerContext context)
    {
        var json = new JsonObject
        {
            ["snapshot"] = snapshots.Latest is { } latest ? SnapshotJsonBuilder.Build(latest, snapshots.Previous, dumps) : null,
            ["hasBaseline"] = snapshots.Baseline != null,
        };
        return HttpResponse.WriteJson(context, json.ToJsonString());
    }
}
