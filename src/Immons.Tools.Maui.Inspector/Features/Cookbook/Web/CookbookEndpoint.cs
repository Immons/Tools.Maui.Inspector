using System.Net;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.Cookbook.Ui;

namespace Immons.Tools.Maui.Inspector.Features.Cookbook.Web;

/// <summary>
/// GET /api/cookbook (catalog), POST /api/cookbook/open {on, section?, page?, item?} (push / pop the
/// page and steer what it shows), POST /api/cookbook/focus {id} (the item on a page of its own — full
/// width, properties underneath; null closes it), GET /api/cookbook/preview?id=[&amp;focus=1] (PNG of one
/// tile — or of the focused full-width instance — with its visual states in the X-Visual-States header)
/// and POST /api/cookbook/state {id, state} (forces a visual state on the sample shown on the device).
/// </summary>
internal sealed class CookbookEndpoint(
    IMainThreadDispatcher mainThread,
    ICookbookHost host,
    CookbookJsonBuilder json,
    TilePreviewer previewer) : IHttpEndpoint
{
    public const string VisualStatesHeader = "X-Visual-States";

    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Cookbook.Catalog)
        {
            var body = await mainThread.RunAsync(() =>
            {
                host.RebuildCatalog();
                return json.Build();
            }).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, body).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Cookbook.Open)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var on = node?["on"]?.GetValue<bool>() ?? true;
            var section = node?["section"]?.GetValue<string>();
            var page = node?["page"]?.GetValue<int>() ?? 0;
            var item = node?["item"]?.GetValue<string>();
            var ok = await mainThread.RunTaskAsync(() => Open(on, section, page, item)).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, new JsonObject { ["ok"] = ok, ["open"] = host.IsOpen }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Cookbook.Focus)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var id = node?["id"]?.GetValue<string>();
            var ok = await mainThread.RunTaskAsync(async () =>
            {
                if (string.IsNullOrEmpty(id))
                {
                    await host.UnfocusAsync();
                    return true;
                }
                return await host.FocusAsync(id); // headless unless the gallery is open on the device
            }).ConfigureAwait(false);
            await HttpResponse.WriteJson(context, new JsonObject { ["ok"] = ok, ["focus"] = host.Focused?.Item.Id }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && path == ApiRoutes.Cookbook.Preview)
        {
            var id = context.Request.QueryString["id"] ?? "";
            var focused = context.Request.QueryString["focus"] == "1";
            var preview = await previewer.RenderAsync(id, focused).ConfigureAwait(false);
            if (preview.Png == null)
            {
                await HttpResponse.WriteText(context, 404, preview.Error ?? "no preview").ConfigureAwait(false);
                return true;
            }
            context.Response.Headers[VisualStatesHeader] = string.Join(",", preview.States);
            context.Response.Headers["Access-Control-Expose-Headers"] = VisualStatesHeader;
            await HttpResponse.WriteBytes(context, "image/png", preview.Png).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Cookbook.State)
        {
            var node = await RequestBody.ReadJson(context).ConfigureAwait(false);
            var id = node?["id"]?.GetValue<string>() ?? "";
            var state = node?["state"]?.GetValue<string>() ?? "";
            var ok = await mainThread.RunTaskAsync(() => GoToState(id, state)).ConfigureAwait(false);
            await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    async Task<bool> Open(bool on, string? section, int page, string? item)
    {
        if (!on)
        {
            await host.CloseAsync();
            return true;
        }
        if (!await host.OpenAsync(null) || host.Page is not { } shown)
            return false;
        if (item != null)
            return await shown.ShowItemAsync(item);
        return section == null || shown.ShowSection(section, page);
    }

    /// <summary>
    /// The state lands on the focused instance (focusing the item first — headlessly when the
    /// gallery is closed), so the next preview captures it; on the device the tile is used when shown.
    /// </summary>
    async Task<bool> GoToState(string id, string state)
    {
        if (host.Page is { } page && page.FindRealized(id) is { Normal: { } shown })
            return VisualStateManager.GoToState(shown, state);
        if (!(host.Focused is { } focused && focused.Item.Id == id) && !await host.FocusAsync(id))
            return false;
        return host.Focused is { Sample: { } sample } && VisualStateManager.GoToState(sample, state);
    }
}
