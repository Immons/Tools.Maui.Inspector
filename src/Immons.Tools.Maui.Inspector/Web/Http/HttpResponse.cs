using System.Text;

namespace Immons.Tools.Maui.Inspector.Web.Http;

/// <summary>Response helpers shared by the endpoints.</summary>
internal static class HttpResponse
{
    public static Task<bool> WriteText(InspectorHttpContext context, int status, string text) =>
        Write(context, status, "text/plain; charset=utf-8", text);

    public static Task<bool> WriteJson(InspectorHttpContext context, string json, int status = 200) =>
        Write(context, status, "application/json", json);

    /// <summary>The canonical {"ok":bool} result.</summary>
    public static Task<bool> WriteOk(InspectorHttpContext context, bool ok, int status = 200) =>
        WriteJson(context, ok ? "{\"ok\":true}" : "{\"ok\":false}", status);

    /// <summary>False when the client was gone — every writer here reports a dropped connection that way.</summary>
    public static Task<bool> Write(InspectorHttpContext context, int status, string contentType, string body) =>
        WriteBytes(context, contentType, Encoding.UTF8.GetBytes(body), status);

    /// <summary>
    /// Sends the whole response. Never throws for a client that went away (closed tab, slept phone,
    /// dropped Wi-Fi) — the send just reports false and the exchange ends.
    /// </summary>
    public static Task<bool> WriteBytes(InspectorHttpContext context, string contentType, byte[] bytes, int status = 200)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        // The panel of one app mirrors edits to sibling apps on other ports — allow it.
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        return context.Response.SendAsync(status, contentType, bytes);
    }
}
