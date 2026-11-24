using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.History;

/// <summary>GET /api/history and POST /api/history/undo.</summary>
internal sealed class HistoryEndpoint(
    IMainThreadDispatcher mainThread,
    IEditHistory history,
    IPropertyCommands commands) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.History.List)
        {
            await HttpResponse.WriteJson(context, history.ToJson()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.History.Redo)
        {
            var ok = await mainThread.RunAsync(commands.Redo).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.History.Undo)
        {
            var seq = (await RequestBody.ReadJson(context).ConfigureAwait(false))?["seq"]?.GetValue<long>() ?? 0;
            var ok = await mainThread.RunAsync(() => commands.Undo(seq)).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        return false;
    }
}
