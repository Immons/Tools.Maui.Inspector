using System.Net;

namespace Immons.Tools.Maui.Inspector.Web.Endpoints;

/// <summary>POST {"on":bool} toggles: modes, overlay, debug paint, perf, slow animations, WYSIWYG.</summary>
internal sealed class ToggleEndpoint(
    IMainThreadDispatcher mainThread,
    IActiveInspectorProvider inspectors,
    IXamlChangeLog xamlChanges) : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Post)
            return false;
        if (path is not (ApiRoutes.Toggles.MeasureMode or ApiRoutes.Toggles.SelectMode or ApiRoutes.Toggles.Overlay
            or ApiRoutes.Toggles.DebugPaint or ApiRoutes.Toggles.Perf or ApiRoutes.Toggles.SlowAnimations or ApiRoutes.Toggles.Wysiwyg))
            return false;

        var on = await RequestBody.ReadOnFlag(context).ConfigureAwait(false);
        var ok = await mainThread.RunAsync(() => Toggle(path, on)).ConfigureAwait(false);
        await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
        return true;
    }

    bool Toggle(string path, bool on)
    {
        switch (path)
        {
            case ApiRoutes.Toggles.Wysiwyg:
                xamlChanges.Enabled = on;
                return true;
            case ApiRoutes.Toggles.Perf:
                FrameStats.SetEnabled(on);
                return true;
            case ApiRoutes.Toggles.SlowAnimations:
                return SlowAnimations.Set(on);
        }

        if (inspectors.Current is not { } inspector)
            return false;

        switch (path)
        {
            case ApiRoutes.Toggles.MeasureMode:
                inspector.SetRemoteMeasureMode(on);
                return true;
            case ApiRoutes.Toggles.SelectMode:
                inspector.SetRemoteSelectMode(on);
                return true;
            case ApiRoutes.Toggles.DebugPaint:
                inspector.SetDebugPaint(on);
                return true;
            default:
                inspector.SetOverlayShown(on);
                return true;
        }
    }
}
