using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.Logging;

/// <summary>GET /api/logs — entries captured by AddMauiInspector().</summary>
internal sealed class LogsEndpoint(ILogSink logs) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Get || path != ApiRoutes.Logs.List)
            return false;

        await HttpResponse.WriteJson(context, logs.ToJson()).ConfigureAwait(false);
        return true;
    }
}
