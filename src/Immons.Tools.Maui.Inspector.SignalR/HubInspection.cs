using System.Net;
using Immons.Tools.Maui.Inspector.Features.SignalR;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>
/// Replaces the builder's <see cref="HubConnection"/> registration with one that wraps the
/// protocol, the connection factory and the retry policy. Everything is resolved when
/// <c>Build()</c> runs, so registrations made after <c>WithMauiInspector</c> (protocol, URL,
/// reconnect policy) are the ones that get wrapped.
/// </summary>
internal static class HubInspection
{
    public static IHubConnectionBuilder Attach(IHubConnectionBuilder builder, string hubName,
        SignalRInspectorOptions options, ISignalRMonitor monitor)
    {
        builder.Services.RemoveAll<HubConnection>();
        builder.Services.AddSingleton(sp => Create(sp, hubName, options, monitor));
        return builder;
    }

    static HubConnection Create(IServiceProvider sp, string hubName, SignalRInspectorOptions options, ISignalRMonitor monitor)
    {
        var protocol = sp.GetRequiredService<IHubProtocol>();
        var connectionFactory = sp.GetRequiredService<IConnectionFactory>();
        var endPoint = sp.GetRequiredService<EndPoint>();
        var loggerFactory = sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        var retryPolicy = sp.GetService<IRetryPolicy>();

        var session = new HubSession(hubName, options, monitor, protocol);
        var inspectingProtocol = new InspectingHubProtocol(protocol, session);
        var inspectingFactory = new InspectingConnectionFactory(connectionFactory, session);

        // The same constructors the container would pick: with a reconnect policy when one is registered.
        var connection = retryPolicy == null
            ? new HubConnection(inspectingFactory, inspectingProtocol, endPoint, sp, loggerFactory)
            : new HubConnection(inspectingFactory, inspectingProtocol, endPoint, sp, loggerFactory,
                new InspectingRetryPolicy(retryPolicy, session));

        session.Attach(connection, endPoint);
        return connection;
    }
}
