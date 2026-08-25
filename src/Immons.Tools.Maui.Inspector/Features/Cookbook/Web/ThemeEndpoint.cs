using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

/// <summary>GET /api/theme and POST /api/theme {theme: "system" | "light" | "dark"} — the app-wide theme override.</summary>
internal sealed class ThemeEndpoint(IMainThreadDispatcher mainThread, ICookbookHost host) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (path != ApiRoutes.Theme.State)
            return false;

        if (method == HttpVerbs.Post)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var theme = node?["theme"]?.GetValue<string>() ?? "";
            var ok = await mainThread.RunAsync(() =>
            {
                var set = AppThemeSwitch.Set(theme);
                host.Page?.RefreshTheme();
                return set;
            }).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Get)
            return false;

        var json = await mainThread.RunAsync(() => new JsonObject
        {
            ["theme"] = AppThemeSwitch.Current,
            ["effective"] = AppThemeSwitch.Effective,
        }.ToJsonString()).ConfigureAwait(false);
        await HttpResponse.WriteJson(context, json).ConfigureAwait(false);
        return true;
    }
}
