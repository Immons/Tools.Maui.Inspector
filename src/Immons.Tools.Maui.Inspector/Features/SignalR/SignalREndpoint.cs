using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Web.Http;

namespace Immons.Tools.Maui.Inspector.Features.SignalR;

/// <summary>
/// GET /api/signalr (hubs + state), GET /api/signalr/events?hub=&amp;since=&amp;method=&amp;limit=,
/// GET /api/signalr/event?seq= (one event with its full payload), POST clear / pause / inject.
/// </summary>
internal sealed class SignalREndpoint(ISignalRMonitor monitor) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (!path.StartsWith(ApiRoutes.SignalR.Hubs, StringComparison.Ordinal))
            return false;

        var query = context.Request.QueryString;
        switch (method, path)
        {
            case (HttpVerbs.Get, ApiRoutes.SignalR.Hubs):
                await HttpResponse.WriteJson(context, monitor.HubsJson()).ConfigureAwait(false);
                return true;

            case (HttpVerbs.Get, ApiRoutes.SignalR.Events):
                long.TryParse(query["since"], out var since);
                int.TryParse(query["limit"], out var limit);
                await HttpResponse.WriteJson(context,
                    monitor.EventsJson(query["hub"], since, query["method"], limit)).ConfigureAwait(false);
                return true;

            case (HttpVerbs.Get, ApiRoutes.SignalR.Event):
                long.TryParse(query["seq"], out var seq);
                if (monitor.EventJson(seq) is { } json)
                    await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
                else
                    await HttpResponse.WriteText(context, 404, "event not found").ConfigureAwait(false);
                return true;

            case (HttpVerbs.Post, ApiRoutes.SignalR.Clear):
            {
                var body = await ReadBody(context).ConfigureAwait(false);
                monitor.Clear(Text(body?["hub"]));
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;
            }

            case (HttpVerbs.Post, ApiRoutes.SignalR.Pause):
            {
                var body = await ReadBody(context).ConfigureAwait(false);
                monitor.Paused = body?["on"] is JsonValue on && on.TryGetValue<bool>(out var flag) ? flag : !monitor.Paused;
                await HttpResponse.WriteJson(context,
                    new JsonObject { ["ok"] = true, ["paused"] = monitor.Paused }.ToJsonString()).ConfigureAwait(false);
                return true;
            }

            case (HttpVerbs.Post, ApiRoutes.SignalR.Inject):
            {
                var body = await ReadBody(context).ConfigureAwait(false);
                var hub = Text(body?["hub"]) ?? "";
                var target = Text(body?["method"]) ?? "";
                // "args" may come as a JSON array or as its text (what the panel's textarea holds).
                var args = body?["args"] switch
                {
                    null => "[]",
                    JsonValue v when v.TryGetValue<string>(out var text) => text,
                    var node => node.ToJsonString(),
                };
                var error = string.IsNullOrWhiteSpace(target) ? "method is required" : monitor.Inject(hub, target.Trim(), args);
                await HttpResponse.WriteJson(context, new JsonObject
                {
                    ["ok"] = error == null,
                    ["error"] = error,
                }.ToJsonString(), error == null ? 200 : 400).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    static async Task<JsonNode?> ReadBody(InspectorHttpContext context)
    {
        try
        {
            return await RequestBody.ReadJson(context).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
