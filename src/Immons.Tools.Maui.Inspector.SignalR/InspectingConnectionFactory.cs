using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>
/// Wraps the transport factory (negotiate + WebSockets/SSE/long polling): every connect attempt,
/// its failure, and the connection that comes out of it — whose reader is where the handshake
/// is observed and where injected messages enter.
/// </summary>
internal sealed class InspectingConnectionFactory(IConnectionFactory inner, HubSession session) : IConnectionFactory
{
    public async ValueTask<ConnectionContext> ConnectAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
    {
        session.OnConnecting();
        ConnectionContext connection;
        try
        {
            connection = await inner.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            session.OnConnectFailed(ex);
            throw;
        }

        try
        {
            return session.OnConnected(connection);
        }
        catch
        {
            // Hand back the real connection rather than failing the app's start.
            return connection;
        }
    }
}

/// <summary>
/// The transport connection with its input reader swapped for <see cref="InspectingPipeReader"/>.
/// Everything else — id, features (keep-alive, stateful reconnect), endpoints, lifetime — is the inner connection's.
/// </summary>
internal sealed class InspectedConnection : ConnectionContext
{
    readonly ConnectionContext _inner;
    readonly Action<InspectedConnection> _disposed;
    int _disposeCalled;

    public InspectedConnection(ConnectionContext inner, InspectingPipeReader reader, Action<InspectedConnection> disposed)
    {
        _inner = inner;
        _disposed = disposed;
        Reader = reader;
        Transport = new DuplexPipe(reader, inner.Transport.Output);
    }

    public InspectingPipeReader Reader { get; }

    public ConnectionContext Inner => _inner;

    public override IDuplexPipe Transport { get; set; }

    public override string ConnectionId
    {
        get => _inner.ConnectionId;
        set => _inner.ConnectionId = value;
    }

    public override IFeatureCollection Features => _inner.Features;

    public override IDictionary<object, object?> Items
    {
        get => _inner.Items;
        set => _inner.Items = value;
    }

    public override CancellationToken ConnectionClosed
    {
        get => _inner.ConnectionClosed;
        set => _inner.ConnectionClosed = value;
    }

    public override EndPoint? LocalEndPoint
    {
        get => _inner.LocalEndPoint;
        set => _inner.LocalEndPoint = value;
    }

    public override EndPoint? RemoteEndPoint
    {
        get => _inner.RemoteEndPoint;
        set => _inner.RemoteEndPoint = value;
    }

    public override void Abort() => _inner.Abort();

    public override void Abort(ConnectionAbortedException abortReason) => _inner.Abort(abortReason);

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Exchange(ref _disposeCalled, 1) == 0)
                _disposed(this);
        }
    }

    sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;

        public PipeWriter Output { get; } = output;
    }
}
