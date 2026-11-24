using System.Net;

namespace Immons.Tools.Maui.Inspector.Web.Endpoints;

/// <summary>GET /api/selection — the web client's 1 s state poll.</summary>
internal sealed class SelectionEndpoint(
    IMainThreadDispatcher mainThread,
    ISelectionJsonBuilder selectionJson) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Get || path != ApiRoutes.Selection.State)
            return false;

        var json = await mainThread.RunAsync(selectionJson.Build).ConfigureAwait(false);
        await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
        return true;
    }
}
