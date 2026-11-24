using System.Net;

namespace Immons.Tools.Maui.Inspector.Web.Http;

/// <summary>
/// One group of related routes. The server walks the registered endpoints in order and the
/// first one that recognizes (method, path) handles the request — adding an API area means
/// adding an implementation, not editing the server.
/// </summary>
internal interface IHttpEndpoint
{
    /// <summary>Handles the request when the route matches; false lets the next endpoint try.</summary>
    Task<bool> TryHandle(InspectorHttpContext context, string method, string path);
}
