using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.VisualTree.Web;

/// <summary>GET /api/tree and GET /api/dump.</summary>
internal sealed class TreeEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors,
    ITreeJsonBuilder treeJson) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Get)
            return false;

        switch (path)
        {
            case ApiRoutes.Tree.List:
                var json = await mainThread.RunAsync(treeJson.Build).ConfigureAwait(false);
                await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
                return true;

            case ApiRoutes.Dump.Text:
                var dump = await mainThread.RunAsync(() =>
                    inspectors.Current?.BuildDump() ?? "no active window").ConfigureAwait(false);
                await HttpResponse.WriteText(context, 200, dump).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }
}
