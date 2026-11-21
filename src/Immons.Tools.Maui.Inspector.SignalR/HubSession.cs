using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.SignalR;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>
/// The inspection of one <see cref="HubConnection"/> instance: turns what the protocol,
/// transport and reconnect wrappers observe into hub state and timeline events, and owns the
/// injection of simulated server calls. Every callback swallows its own failures — inspecting
/// must never break the app's connection.
/// </summary>
internal sealed class HubSession : ISignalRInjector
{
    const int MaxPendingInvocations = 2000;
    const int MaxQueuedInjections = 50;

    readonly ISignalRMonitor _monitor;
    readonly SignalRHub _hub;
    readonly SignalRInspectorOptions _options;
    readonly PayloadMasker _masker;
    readonly bool _textProtocol;
    readonly string _protocolName;
    readonly ConcurrentDictionary<string, (string Method, long Started)> _pending = new();
    volatile InspectedConnection? _current;
    volatile bool _injectedInFlight;

    public HubSession(string hubName, SignalRInspectorOptions options, ISignalRMonitor monitor, IHubProtocol protocol)
    {
        _monitor = monitor;
        _options = options;
        _masker = new PayloadMasker(options.MaskedFields);
        _textProtocol = protocol.TransferFormat == TransferFormat.Text;
        _protocolName = protocol.Name;
        _hub = monitor.Hub(hubName, options.MaxEvents);
        _hub.OnInstanceCreated();
        _hub.Protocol = $"{protocol.Name} v{protocol.Version}";
    }

    public SignalRHub Hub => _hub;

    /// <summary>Bytes of an outgoing frame copied for the preview (0 = none: payloads off or a binary protocol).</summary>
    public int CaptureLimit => _options.CapturePayloads && _textProtocol ? PayloadMasker.MaxParsedBytes : 0;

    /// <summary>True while the receive loop parses a frame that came from <see cref="Inject"/>.</summary>
    public bool InjectedFrameInFlight
    {
        get => _injectedInFlight;
        set => _injectedInFlight = value;
    }

    public void Attach(HubConnection connection, EndPoint endPoint)
    {
        _hub.Url = _masker.MaskUrl(endPoint is UriEndPoint uri ? uri.Uri.ToString() : endPoint.ToString() ?? "");
        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;
        connection.Closed += OnClosed;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    Task OnReconnecting(Exception? error)
    {
        Safe(() => _monitor.RecordState(_hub, SignalRState.Reconnecting, error: error?.Message, note: "Reconnecting"));
        return Task.CompletedTask;
    }

    Task OnReconnected(string? connectionId)
    {
        Safe(() => _monitor.RecordState(_hub, SignalRState.Connected, connectionId, note: "Reconnected"));
        return Task.CompletedTask;
    }

    Task OnClosed(Exception? error)
    {
        Safe(() =>
        {
            _current = null;
            _monitor.RecordState(_hub, SignalRState.Disconnected, error: error?.Message,
                unexpectedLoss: error != null, note: error == null ? "Closed" : "Closed with error");
        });
        return Task.CompletedTask;
    }

    public void OnConnecting() => Safe(() =>
    {
        // During an automatic reconnect the retry event already announced the attempt.
        if (_hub.State != SignalRState.Reconnecting)
            _monitor.RecordState(_hub, SignalRState.Connecting, note: "Connecting");
    });

    public void OnConnectFailed(Exception error) => Safe(() =>
    {
        if (_hub.State == SignalRState.Reconnecting)
            Record(new SignalREvent
            {
                Hub = _hub.Name,
                Time = DateTimeOffset.Now,
                Kind = SignalRKinds.Error,
                State = "Reconnect attempt failed",
                Error = Describe(error),
                Attempt = _hub.Attempt,
            });
        else
            _monitor.RecordState(_hub, SignalRState.Disconnected, error: Describe(error), note: "Start failed");
    });

    public ConnectionContext OnConnected(ConnectionContext connection)
    {
        var reader = new InspectingPipeReader(connection.Transport.Input, this)
        {
            StatefulReconnect = HasStatefulReconnect(connection),
        };
        var wrapped = new InspectedConnection(connection, reader, OnTransportDisposed);
        _pending.Clear();
        _current = wrapped;
        Safe(() =>
        {
            _hub.Transport = TransportName(connection);
            // The most recently connected instance answers the panel's injections.
            _hub.UseInjector(this);
        });
        return wrapped;
    }

    public void OnHandshakeCompleted(ReadOnlySequence<byte> response) => Safe(() =>
    {
        var error = HandshakeError(response);
        if (error != null)
        {
            if (_hub.State == SignalRState.Reconnecting)
                Record(new SignalREvent
                {
                    Hub = _hub.Name, Time = DateTimeOffset.Now, Kind = SignalRKinds.Error,
                    State = "Handshake failed", Error = error, Attempt = _hub.Attempt,
                });
            else
                _monitor.RecordState(_hub, SignalRState.Disconnected, error: error, note: "Handshake failed");
            return;
        }

        // A reconnect becomes Connected through the Reconnected event, which follows right after.
        if (_hub.State != SignalRState.Reconnecting)
            _monitor.RecordState(_hub, SignalRState.Connected, _current?.ConnectionId, note: "Connected");
    });

    void OnTransportDisposed(InspectedConnection connection) => Safe(() =>
    {
        if (ReferenceEquals(_current, connection))
            _current = null;
        if (_hub.State == SignalRState.Connecting)
            _monitor.RecordState(_hub, SignalRState.Disconnected,
                error: connection.Reader.HandshakeDone ? null : "the connection closed before the handshake completed",
                note: "Start failed");
    });

    public void OnRetryScheduled(RetryContext context, TimeSpan? delay) => Safe(() =>
    {
        var attempt = (int)Math.Min(int.MaxValue, context.PreviousRetryCount + 1);
        if (delay != null)
            _hub.SetState(SignalRState.Reconnecting, null, null, attempt);
        Record(new SignalREvent
        {
            Hub = _hub.Name,
            Time = DateTimeOffset.Now,
            Kind = SignalRKinds.Retry,
            State = delay == null
                ? $"Giving up after {context.PreviousRetryCount} attempt(s), {context.ElapsedTime.TotalSeconds:0.#} s"
                : $"Attempt {attempt} in {FormatDelay(delay.Value)}",
            Error = context.RetryReason?.Message,
            Attempt = delay == null ? null : attempt,
        });
    });

    // ── Messages ────────────────────────────────────────────────────────────────

    public void OnIncoming(HubMessage message, ReadOnlySequence<byte> frame)
    {
        try
        {
            var injected = _injectedInFlight;
            var info = MessageDescriber.Describe(message);
            var now = DateTimeOffset.Now;
            var bytes = (int)Math.Min(int.MaxValue, frame.Length);
            _hub.CountMessage(SignalRDirection.In, bytes, info.Kind == SignalRKinds.Ping, now);
            if (info.Kind == SignalRKinds.Ping && !_options.CapturePings || _monitor.Paused)
                return;

            var method = info.Method;
            double? duration = null;
            if (info.InvocationId is { } id)
            {
                switch (info.Kind)
                {
                    case SignalRKinds.Completion when _pending.TryRemove("out:" + id, out var call):
                        method = call.Method;
                        duration = Stopwatch.GetElapsedTime(call.Started).TotalMilliseconds;
                        break;
                    case SignalRKinds.StreamItem when _pending.TryGetValue("out:" + id, out var call):
                        method = call.Method;
                        break;
                    case SignalRKinds.Invocation when method != null:
                        // A server → client call that expects a result (client results).
                        Track("in:" + id, method);
                        break;
                }
            }

            var (payload, truncated) = Payload(message, frame);
            Record(new SignalREvent
            {
                Hub = _hub.Name,
                Time = now,
                Direction = SignalRDirection.In,
                Kind = info.Kind,
                Method = method,
                InvocationId = info.InvocationId,
                Bytes = bytes,
                Payload = payload,
                Truncated = truncated,
                Error = info.Error,
                DurationMs = duration,
                Injected = injected,
            });
        }
        catch
        {
            // never let the preview break the receive loop
        }
    }

    public void OnOutgoing(HubMessage message, ReadOnlySpan<byte> captured, int written)
    {
        try
        {
            var info = MessageDescriber.Describe(message);
            var now = DateTimeOffset.Now;
            _hub.CountMessage(SignalRDirection.Out, written, info.Kind == SignalRKinds.Ping, now);

            var method = info.Method;
            double? duration = null;
            if (info.InvocationId is { } id)
            {
                switch (info.Kind)
                {
                    case SignalRKinds.Invocation or SignalRKinds.StreamInvocation when method != null:
                        Track("out:" + id, method);
                        break;
                    case SignalRKinds.Completion when _pending.TryRemove("in:" + id, out var call):
                        method = call.Method;
                        duration = Stopwatch.GetElapsedTime(call.Started).TotalMilliseconds;
                        break;
                    case SignalRKinds.CancelInvocation when _pending.TryRemove("out:" + id, out var call):
                        method = call.Method;
                        break;
                }
            }

            if (info.Kind == SignalRKinds.Ping && !_options.CapturePings || _monitor.Paused)
                return;

            string? payload = null;
            var truncated = false;
            if (_options.CapturePayloads)
            {
                if (!_textProtocol)
                    (payload, truncated) = _masker.FromNode(MessageDescriber.ObjectPayload(message), _options.MaxPayloadChars);
                else if (captured.Length == written)
                    (payload, truncated) = _masker.FromJsonUtf8(captured, _options.MaxPayloadChars);
                else
                    (payload, truncated) = ($"({written} bytes — too large to preview)", true);
            }

            Record(new SignalREvent
            {
                Hub = _hub.Name,
                Time = now,
                Direction = SignalRDirection.Out,
                Kind = info.Kind,
                Method = method,
                InvocationId = info.InvocationId,
                Bytes = written,
                Payload = payload,
                Truncated = truncated,
                Error = info.Error,
                DurationMs = duration,
            });
        }
        catch
        {
            // never let the preview break a send
        }
    }

    public void OnParseError(Exception error, long length) => Safe(() => Record(new SignalREvent
    {
        Hub = _hub.Name,
        Time = DateTimeOffset.Now,
        Direction = SignalRDirection.In,
        Kind = SignalRKinds.Error,
        Bytes = (int)Math.Min(int.MaxValue, length),
        Error = "could not parse the message: " + error.Message,
    }));

    (string? Payload, bool Truncated) Payload(HubMessage message, ReadOnlySequence<byte> frame)
    {
        if (!_options.CapturePayloads)
            return (null, false);
        if (!_textProtocol)
            return _masker.FromNode(MessageDescriber.ObjectPayload(message), _options.MaxPayloadChars);
        if (frame.Length > PayloadMasker.MaxParsedBytes)
            return ($"({frame.Length} bytes — too large to preview)", true);
        return frame.IsSingleSegment
            ? _masker.FromJsonUtf8(frame.FirstSpan, _options.MaxPayloadChars)
            : _masker.FromJsonUtf8(frame.ToArray(), _options.MaxPayloadChars);
    }

    void Track(string key, string method)
    {
        if (_pending.Count >= MaxPendingInvocations)
            _pending.Clear();   // invocations that never completed — keep the map bounded
        _pending[key] = (method, Stopwatch.GetTimestamp());
    }

    void Record(SignalREvent item)
    {
        if (_current is { } connection && item.ConnectionId == null)
        {
            try
            {
                item = item with { ConnectionId = connection.ConnectionId };
            }
            catch
            {
                // the transport may already be disposed
            }
        }
        _monitor.Record(_hub, item);
    }

    // ── Injection ───────────────────────────────────────────────────────────────

    public string? InjectionBlocker
    {
        get
        {
            if (!_options.AllowInjection)
                return "injection is off for this hub (AllowInjection = false)";
            if (!_textProtocol)
                return $"injection needs a text protocol (JSON / Newtonsoft); this hub uses {_protocolName}";
            var connection = _current;
            if (connection == null || _hub.State != SignalRState.Connected || !connection.Reader.HandshakeDone)
                return "the hub is not connected";
            if (connection.Reader.StatefulReconnect)
                return "not available with stateful reconnect — it would break the server's message acknowledgements";
            return null;
        }
    }

    /// <summary>
    /// Queues {"type":1,"target":method,"arguments":[…]} on the live connection's input, as if the
    /// server had sent it. No invocation id, so the client never answers the server about it.
    /// </summary>
    public string? Inject(string method, string argumentsJson)
    {
        if (InjectionBlocker is { } blocker)
            return blocker;

        JsonNode? parsed;
        try
        {
            parsed = string.IsNullOrWhiteSpace(argumentsJson) ? new JsonArray() : JsonNode.Parse(argumentsJson);
        }
        catch (JsonException ex)
        {
            return "arguments are not valid JSON: " + ex.Message;
        }

        // A single value is one argument; an array is the argument list.
        var arguments = parsed as JsonArray ?? new JsonArray(parsed);
        var frame = new JsonObject
        {
            ["type"] = HubProtocolConstants.InvocationMessageType,
            ["target"] = method,
            ["arguments"] = arguments,
        };

        var connection = _current;
        if (connection == null)
            return "the hub is not connected";
        if (connection.Reader.PendingInjections >= MaxQueuedInjections)
            return "too many injected messages are still waiting to be delivered";
        connection.Reader.Inject(Encoding.UTF8.GetBytes(frame.ToJsonString() + '\u001e'));
        return null;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    static string? HandshakeError(ReadOnlySequence<byte> response)
    {
        try
        {
            var bytes = response.ToArray();
            var length = bytes.Length > 0 && bytes[^1] == 0x1E ? bytes.Length - 1 : bytes.Length;
            if (length == 0)
                return null;
            return JsonNode.Parse(bytes.AsSpan(0, length)) is JsonObject obj
                   && obj["error"] is JsonValue value && value.TryGetValue<string>(out var error)
                ? error
                : null;
        }
        catch
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Best-effort label; null when the field is gone.")]
    static string? TransportName(ConnectionContext connection)
    {
        try
        {
            var field = connection.GetType().GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance);
            var name = field?.GetValue(connection)?.GetType().Name;
            return name?.Replace("Transport", "", StringComparison.Ordinal);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>By name: IStatefulReconnectFeature is a preview interface, and only its presence matters.</summary>
    static bool HasStatefulReconnect(ConnectionContext connection)
    {
        try
        {
            return connection.Features.Any(f => f.Key.Name == "IStatefulReconnectFeature" && f.Value != null);
        }
        catch
        {
            return true;   // unknown — err on the side of not injecting
        }
    }

    static string Describe(Exception error) =>
        error is AggregateException { InnerException: { } inner } ? inner.Message : error.Message;

    static string FormatDelay(TimeSpan delay) =>
        delay.TotalSeconds < 1 ? $"{delay.TotalMilliseconds:0} ms" : $"{delay.TotalSeconds:0.#} s";

    static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // inspection must never break the app's connection
        }
    }
}
