namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>What the SignalR view records for one hub (see <c>WithMauiInspector</c>).</summary>
public sealed class SignalRInspectorOptions
{
    /// <summary>
    /// Record message contents (method arguments, results, stream items). Off = only the method,
    /// direction, invocation id and size are kept. Default: true.
    /// </summary>
    public bool CapturePayloads { get; set; } = true;

    /// <summary>
    /// Longest payload kept per message, in characters of JSON. Anything longer is cut and marked
    /// as truncated. Default: 4096.
    /// </summary>
    public int MaxPayloadChars { get; set; } = 4096;

    /// <summary>
    /// Field names whose values are replaced with "***" before anything is stored, on top of the
    /// built-in list (token, password, secret, authorization, apikey, cookie, credential, pin, otp…).
    /// Matched case-insensitively, ignoring "_", "-" and "."; a name matches when it contains the entry.
    /// </summary>
    public ICollection<string> MaskedFields { get; } = new List<string>();

    /// <summary>
    /// Show keep-alive pings on the timeline. They are always counted (and the last ping time shown);
    /// off keeps a chatty timeline readable. Default: true.
    /// </summary>
    public bool CapturePings { get; set; } = true;

    /// <summary>Events kept for this hub; the oldest are overwritten beyond it. Default: 1000.</summary>
    public int MaxEvents { get; set; } = 1000;

    /// <summary>
    /// Lets the panel simulate a server → client call on this hub ("Inject"): the message enters the
    /// connection's receive path as if the server had sent it, so the app's <c>On(...)</c> handler runs
    /// for real. A development tool — only for text (JSON / Newtonsoft) protocols and not with stateful
    /// reconnect. Default: true.
    /// </summary>
    public bool AllowInjection { get; set; } = true;
}
