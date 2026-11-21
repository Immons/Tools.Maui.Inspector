namespace Immons.Tools.Maui.Inspector.Features.SignalR;

/// <summary>
/// Simulates a server → client call on a live connection (the panel's "inject incoming message").
/// Implemented by the SignalR add-on package, which owns the connection.
/// </summary>
internal interface ISignalRInjector
{
    /// <summary>Null when a message can be injected right now, otherwise the reason it cannot.</summary>
    string? InjectionBlocker { get; }

    /// <summary>Queues an invocation of the client method; returns an error message or null.</summary>
    string? Inject(string method, string argumentsJson);
}

/// <summary>
/// Everything the panel knows about one named hub: the current connection state, counters and
/// the bounded event timeline. Apps that rebuild their HubConnection after a failure (a new
/// instance per attempt) keep one entry per name — the timeline spans all instances.
/// </summary>
internal sealed class SignalRHub(string name, int capacity)
{
    readonly object _gate = new();
    WeakReference<ISignalRInjector>? _injector;

    public string Name { get; } = name;

    public SignalREventBuffer Events { get; } = new(capacity);

    public SignalRState State { get; private set; } = SignalRState.Disconnected;

    public DateTimeOffset StateSince { get; private set; } = DateTimeOffset.Now;

    public string? ConnectionId { get; private set; }

    public string? Url { get; set; }

    public string? Protocol { get; set; }

    public string? Transport { get; set; }

    /// <summary>Times the connection was lost and came back (automatic reconnects and app-driven restarts).</summary>
    public int Reconnects { get; private set; }

    /// <summary>Current reconnect attempt (0 when connected).</summary>
    public int Attempt { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>HubConnection instances built under this name.</summary>
    public int Instances { get; private set; }

    public long MessagesIn { get; private set; }

    public long MessagesOut { get; private set; }

    public long BytesIn { get; private set; }

    public long BytesOut { get; private set; }

    public DateTimeOffset? LastPingIn { get; private set; }

    public DateTimeOffset? LastPingOut { get; private set; }

    /// <summary>True after a connection loss until it is up again — a fresh Connected then counts as a reconnect.</summary>
    bool _lostUnexpectedly;

    public object Gate => _gate;

    public void OnInstanceCreated()
    {
        lock (_gate)
            Instances++;
    }

    public void CountMessage(SignalRDirection direction, int bytes, bool ping, DateTimeOffset time)
    {
        lock (_gate)
        {
            if (direction == SignalRDirection.In)
            {
                MessagesIn++;
                BytesIn += bytes;
                if (ping)
                    LastPingIn = time;
            }
            else
            {
                MessagesOut++;
                BytesOut += bytes;
                if (ping)
                    LastPingOut = time;
            }
        }
    }

    /// <summary>
    /// Applies a state change; returns false when it is not a change (no event needed).
    /// <paramref name="unexpectedLoss"/> (Disconnected only): true = the connection dropped, false = a
    /// deliberate stop, null = keep what is known (a failed start while the app retries after a drop).
    /// </summary>
    public bool SetState(SignalRState state, string? connectionId, string? error, int? attempt, bool? unexpectedLoss = null)
    {
        lock (_gate)
        {
            var changed = state != State || attempt != null && attempt != Attempt || connectionId != null && connectionId != ConnectionId;
            if (state == SignalRState.Reconnecting && State != SignalRState.Reconnecting)
                Reconnects++;
            if (state == SignalRState.Connected)
            {
                // An app-driven restart after a loss (a new HubConnection) is a reconnect too.
                if (_lostUnexpectedly && State != SignalRState.Reconnecting)
                    Reconnects++;
                _lostUnexpectedly = false;
                Attempt = 0;
                LastError = null;
            }
            else if (attempt != null)
                Attempt = attempt.Value;

            if (state == SignalRState.Disconnected)
            {
                if (unexpectedLoss != null)
                    _lostUnexpectedly = unexpectedLoss.Value;
                Attempt = 0;
            }
            if (state != State)
                StateSince = DateTimeOffset.Now;
            State = state;
            if (connectionId != null || state == SignalRState.Disconnected)
                ConnectionId = connectionId;
            if (error != null)
                LastError = error;
            return changed;
        }
    }

    public void ResetCounters()
    {
        lock (_gate)
        {
            MessagesIn = MessagesOut = BytesIn = BytesOut = 0;
            Reconnects = 0;
        }
    }

    public void UseInjector(ISignalRInjector? injector) =>
        _injector = injector == null ? null : new WeakReference<ISignalRInjector>(injector);

    public ISignalRInjector? Injector => _injector != null && _injector.TryGetTarget(out var target) ? target : null;
}
