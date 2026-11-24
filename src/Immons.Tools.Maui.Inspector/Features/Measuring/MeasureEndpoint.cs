using System.Net;

namespace Immons.Tools.Maui.Inspector.Features.Measuring;

/// <summary>POST /api/measure (primary + compare) and POST /api/clear (highlight off).</summary>
internal sealed class MeasureEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors,
    IElementRegistry elements) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Post)
            return false;

        if (path == ApiRoutes.Measure.Compute)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var primary = node?["primary"]?.GetValue<int>() ?? 0;
            var compare = node?["compare"]?.GetValue<int>() ?? 0;
            var ok = await mainThread.RunAsync(() => Measure(primary, compare)).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        if (path == ApiRoutes.Measure.Clear)
        {
            await mainThread.RunAsync(() =>
            {
                inspectors.Current?.RemoteClearHighlight();
                return true;
            }).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, true).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    bool Measure(int primaryId, int compareId)
    {
        if (inspectors.Current is not { } inspector || elements.Find(primaryId) is not { } primary)
            return false;
        inspector.RemoteMeasure(primary, compareId > 0 ? elements.Find(compareId) : null);
        return true;
    }
}
