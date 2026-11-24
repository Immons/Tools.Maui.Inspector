using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Web.Http;

/// <summary>Request-body helpers shared by the endpoints.</summary>
internal static class RequestBody
{
    public static async Task<JsonNode?> ReadJson(InspectorHttpContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        return body.Length == 0 ? null : JsonNode.Parse(body);
    }

    public static async Task<bool> ReadOnFlag(InspectorHttpContext context) =>
        (await ReadJson(context).ConfigureAwait(false))?["on"]?.GetValue<bool>() ?? false;
}
