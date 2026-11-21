using Immons.Tools.Maui.Inspector.Inspector;
using Immons.Tools.Maui.Inspector.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Immons.Tools.Maui.Inspector;

/// <summary>Hooks a SignalR hub into the inspector's SignalR view.</summary>
public static class MauiInspectorSignalRExtensions
{
    /// <summary>
    /// Shows this hub in the web panel's SignalR view: connection state and reconnects, and every
    /// message in both directions (method, invocation id, size, masked and size-capped arguments).
    /// Call it anywhere in the builder chain — the order against <c>WithUrl</c>,
    /// <c>WithAutomaticReconnect</c> or <c>AddMessagePackProtocol</c> does not matter.
    /// <para>
    /// Does nothing unless <c>UseMauiInspector</c> was called, so the line can stay in code that
    /// also runs with the inspector off. The bytes on the wire are never changed.
    /// </para>
    /// </summary>
    /// <param name="builder">The hub connection builder.</param>
    /// <param name="hubName">Name the panel shows ("chat"); connections built under the same name share one timeline.</param>
    /// <param name="configure">Optional recording settings.</param>
    public static IHubConnectionBuilder WithMauiInspector(
        this IHubConnectionBuilder builder,
        string hubName,
        Action<SignalRInspectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!MauiInspector.IsEnabled)
            return builder;

        var options = new SignalRInspectorOptions();
        configure?.Invoke(options);
        try
        {
            return HubInspection.Attach(builder, string.IsNullOrWhiteSpace(hubName) ? "hub" : hubName.Trim(),
                options, InspectorServices.Current.SignalR);
        }
        catch
        {
            // Inspection is a convenience — never a reason for the app's connection to fail.
            return builder;
        }
    }
}
