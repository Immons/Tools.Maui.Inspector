using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.XamlSync;

/// <summary>
/// GET /api/changes?since= — polled by the sync tool. POST /api/changes/ack — the
/// tool's per-change write results; GET /api/changes/status?seq= — the panel's per-field
/// spinner asks here whether its edit reached the file.
/// </summary>
internal sealed class ChangesEndpoint(IXamlChangeLog xamlChanges, ISyncTracker sync) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Post && path == ApiRoutes.Changes.Ack)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            foreach (var result in node?["results"] as JsonArray ?? [])
            {
                if (result == null)
                    continue;
                xamlChanges.AckWrite(
                    result["seq"]?.GetValue<long>() ?? 0,
                    result["ok"]?.GetValue<bool>() ?? false,
                    result["message"]?.GetValue<string>() ?? "");
            }
            await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && path == ApiRoutes.Changes.Status)
        {
            long seq = 0;
            long.TryParse(context.Request.QueryString["seq"], out seq);
            var (state, message) = xamlChanges.WriteStatus(seq);
            await HttpResponse.WriteJson(context, new JsonObject
            {
                ["state"] = state,
                ["message"] = message,
            }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Get || path != ApiRoutes.Changes.List)
            return false;

        sync.MarkPolled();
        long since = 0;
        if (long.TryParse(context.Request.QueryString["since"], out var parsed))
            since = parsed;
        var includeStructural = (context.Request.QueryString["caps"] ?? "").Contains("el", StringComparison.Ordinal);
        await HttpResponse.WriteJson(context, xamlChanges.ToJson(since, includeStructural)).ConfigureAwait(false);
        return true;
    }
}
