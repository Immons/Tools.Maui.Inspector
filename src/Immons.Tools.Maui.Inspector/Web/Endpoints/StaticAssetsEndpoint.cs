using System.Net;

namespace Immons.Tools.Maui.Inspector.Web.Endpoints;

/// <summary>Serves the embedded web client: index, stylesheet and the /js/*.js modules.</summary>
internal sealed class StaticAssetsEndpoint : IHttpEndpoint
{
    public async Task<bool> TryHandle(InspectorHttpContext context, string method, string path)
    {
        if (method != HttpVerbs.Get)
            return false;

        var (asset, contentType) = path switch
        {
            "/" => ("index.html", "text/html; charset=utf-8"),
            ApiRoutes.Assets.Css => ("app.css", "text/css; charset=utf-8"),
            _ when path.StartsWith(ApiRoutes.Assets.JsPrefix, StringComparison.Ordinal) && path.EndsWith(ApiRoutes.Assets.JsSuffix, StringComparison.Ordinal)
                => ($"js.{path[4..]}", "text/javascript; charset=utf-8"),
            _ => (null, null),
        };

        if (asset == null || contentType == null || WebAssets.Read(asset) is not { } body)
            return false;

        await HttpResponse.Write(context, 200, contentType, body).ConfigureAwait(false);
        return true;
    }
}
