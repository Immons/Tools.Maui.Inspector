using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>Breakpoint control: GET /api/intercept, POST …/config, …/resume, …/abort.</summary>
internal sealed class InterceptEndpoint(IBreakpointGate breakpoints) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Intercept.State)
        {
            await HttpResponse.WriteJson(context, StateJson()).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Post || !path.StartsWith(ApiRoutes.Intercept.Prefix, StringComparison.Ordinal))
            return false;

        var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
        switch (path)
        {
            case ApiRoutes.Intercept.Config:
                breakpoints.Configure(
                    (bool?)node?["req"] ?? false,
                    (bool?)node?["resp"] ?? false,
                    (string?)node?["filter"] ?? "");
                await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
                return true;

            case ApiRoutes.Intercept.Resume:
                var resumed = breakpoints.Resume(
                    (long?)node?["id"] ?? 0,
                    (string?)node?["body"],
                    (int?)node?["status"]);
                await HttpResponse.WriteOk(context, resumed).ConfigureAwait(false);
                return true;

            case ApiRoutes.Intercept.Abort:
                var aborted = breakpoints.Abort((long?)node?["id"] ?? 0);
                await HttpResponse.WriteOk(context, aborted).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    string StateJson()
    {
        var pending = new JsonArray();
        foreach (var call in breakpoints.Pending)
        {
            pending.Add(new JsonObject
            {
                ["id"] = call.Id,
                ["phase"] = call.Phase == InterceptPhase.Request ? "request" : "response",
                ["method"] = call.Method,
                ["url"] = call.Url,
                ["status"] = call.Status,
                ["body"] = call.Body,
                ["time"] = call.Time,
            });
        }

        return new JsonObject
        {
            ["req"] = breakpoints.CaptureRequests,
            ["resp"] = breakpoints.CaptureResponses,
            ["filter"] = breakpoints.UrlFilter,
            ["pending"] = pending,
        }.ToJsonString();
    }
}
