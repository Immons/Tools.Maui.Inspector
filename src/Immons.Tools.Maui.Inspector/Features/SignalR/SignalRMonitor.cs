using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.SignalR;

/// <summary>
/// Sink of the SignalR view: the hubs the app registered with <c>WithMauiInspector(name)</c>,
/// their state and their message timelines. Fed by the Immons.Tools.Maui.Inspector.SignalR
/// package; the core inspector only stores and serves.
/// </summary>
internal interface ISignalRMonitor
{
    /// <summary>When on, protocol messages are not recorded (state changes and counters still are).</summary>
    bool Paused { get; set; }

    /// <summary>The hub with this name, created on first use with room for <paramref name="capacity"/> events.</summary>
    SignalRHub Hub(string name, int capacity = SignalRMonitor.DefaultCapacity);

    IReadOnlyList<SignalRHub> Hubs { get; }

    long LastSeq { get; }

    /// <summary>Stores a message event (skipped while paused) and returns its sequence number, 0 when skipped.</summary>
    long Record(SignalRHub hub, SignalREvent item);

    /// <summary>Applies a state change and records it on the timeline when it is one.</summary>
    void RecordState(SignalRHub hub, SignalRState state, string? connectionId = null, string? error = null,
        int? attempt = null, bool? unexpectedLoss = null, string? note = null);

    void Clear(string? hub);

    string HubsJson();

    string EventsJson(string? hub, long since, string? method, int limit);

    string? EventJson(long seq);

    /// <summary>Simulated server → client call; returns an error, or null once it is queued.</summary>
    string? Inject(string hub, string method, string argumentsJson);
}

internal sealed class SignalRMonitor : ISignalRMonitor
{
    public const int DefaultCapacity = 1000;
    const int PreviewChars = 140;

    readonly object _gate = new();
    readonly List<SignalRHub> _hubs = [];
    long _seq;

    public bool Paused { get; set; }

    public long LastSeq => Interlocked.Read(ref _seq);

    public IReadOnlyList<SignalRHub> Hubs
    {
        get
        {
            lock (_gate)
                return _hubs.ToList();
        }
    }

    public SignalRHub Hub(string name, int capacity = DefaultCapacity)
    {
        lock (_gate)
        {
            var hub = _hubs.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.Ordinal));
            if (hub == null)
            {
                hub = new SignalRHub(name, Math.Max(1, capacity));
                _hubs.Add(hub);
            }
            return hub;
        }
    }

    public long Record(SignalRHub hub, SignalREvent item)
    {
        if (Paused && item.Kind != SignalRKinds.State)
            return 0;
        var seq = Interlocked.Increment(ref _seq);
        hub.Events.Add(item with { Seq = seq, Hub = hub.Name });
        return seq;
    }

    public void RecordState(SignalRHub hub, SignalRState state, string? connectionId = null, string? error = null,
        int? attempt = null, bool? unexpectedLoss = null, string? note = null)
    {
        if (!hub.SetState(state, connectionId, error, attempt, unexpectedLoss) && error == null && note == null)
            return;
        Record(hub, new SignalREvent
        {
            Hub = hub.Name,
            Time = DateTimeOffset.Now,
            Kind = SignalRKinds.State,
            State = note ?? state.ToString(),
            ConnectionId = connectionId ?? hub.ConnectionId,
            Error = error,
            Attempt = attempt,
        });
    }

    public void Clear(string? hub)
    {
        foreach (var h in Hubs)
        {
            if (string.IsNullOrEmpty(hub) || h.Name == hub)
            {
                h.Events.Clear();
                h.ResetCounters();
            }
        }
    }

    public string? Inject(string hub, string method, string argumentsJson)
    {
        var target = Hubs.FirstOrDefault(h => h.Name == hub);
        if (target == null)
            return $"no hub named '{hub}'";
        if (target.Injector is not { } injector)
            return "the hub has no live connection";
        return injector.InjectionBlocker ?? injector.Inject(method, argumentsJson);
    }

    public string HubsJson()
    {
        var array = new JsonArray();
        foreach (var hub in Hubs)
        {
            lock (hub.Gate)
            {
                var injector = hub.Injector;
                array.Add(new JsonObject
                {
                    ["name"] = hub.Name,
                    ["state"] = hub.State.ToString(),
                    ["stateSince"] = hub.StateSince.ToUnixTimeMilliseconds(),
                    ["connectionId"] = hub.ConnectionId,
                    ["url"] = hub.Url,
                    ["protocol"] = hub.Protocol,
                    ["transport"] = hub.Transport,
                    ["reconnects"] = hub.Reconnects,
                    ["attempt"] = hub.Attempt,
                    ["lastError"] = hub.LastError,
                    ["instances"] = hub.Instances,
                    ["messagesIn"] = hub.MessagesIn,
                    ["messagesOut"] = hub.MessagesOut,
                    ["bytesIn"] = hub.BytesIn,
                    ["bytesOut"] = hub.BytesOut,
                    ["lastPingIn"] = hub.LastPingIn?.ToUnixTimeMilliseconds(),
                    ["lastPingOut"] = hub.LastPingOut?.ToUnixTimeMilliseconds(),
                    ["events"] = hub.Events.Count,
                    ["capacity"] = hub.Events.Capacity,
                    ["dropped"] = hub.Events.Dropped,
                    ["injectBlocker"] = injector == null ? "no live connection" : injector.InjectionBlocker,
                });
            }
        }
        return new JsonObject
        {
            ["paused"] = Paused,
            ["lastSeq"] = LastSeq,
            ["hubs"] = array,
        }.ToJsonString();
    }

    public string EventsJson(string? hub, long since, string? method, int limit)
    {
        limit = limit <= 0 ? 500 : Math.Min(limit, 5000);
        var events = new List<SignalREvent>();
        foreach (var h in Hubs)
        {
            if (string.IsNullOrEmpty(hub) || h.Name == hub)
                events.AddRange(h.Events.Since(since));
        }
        if (!string.IsNullOrEmpty(method))
            events.RemoveAll(e => e.Method == null || !e.Method.Contains(method, StringComparison.OrdinalIgnoreCase));
        events.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        // The newest ones win when the caller asks for fewer than there are.
        if (events.Count > limit)
            events.RemoveRange(0, events.Count - limit);

        var array = new JsonArray();
        foreach (var e in events)
            array.Add(ToJson(e, full: false));
        return new JsonObject { ["lastSeq"] = LastSeq, ["events"] = array }.ToJsonString();
    }

    public string? EventJson(long seq)
    {
        foreach (var h in Hubs)
        {
            if (h.Events.Find(seq) is { } e)
                return ToJson(e, full: true).ToJsonString();
        }
        return null;
    }

    static JsonObject ToJson(SignalREvent e, bool full)
    {
        var json = new JsonObject
        {
            ["seq"] = e.Seq,
            ["hub"] = e.Hub,
            ["time"] = e.Time.ToUnixTimeMilliseconds(),
            ["dir"] = e.Direction switch { SignalRDirection.Out => "out", SignalRDirection.In => "in", _ => "" },
            ["kind"] = e.Kind,
        };
        if (e.Method != null) json["method"] = e.Method;
        if (e.InvocationId != null) json["invocationId"] = e.InvocationId;
        if (e.Bytes > 0) json["bytes"] = e.Bytes;
        if (e.Error != null) json["error"] = e.Error;
        if (e.DurationMs != null) json["ms"] = Math.Round(e.DurationMs.Value, 1);
        if (e.State != null) json["state"] = e.State;
        if (e.Attempt != null) json["attempt"] = e.Attempt;
        if (e.Injected) json["injected"] = true;
        if (e.ConnectionId != null) json["connectionId"] = e.ConnectionId;
        if (e.Truncated) json["truncated"] = true;
        if (e.Payload != null)
        {
            if (full)
                json["payload"] = e.Payload;
            else
                json["preview"] = PayloadMasker.Truncate(e.Payload, PreviewChars).Text;
        }
        return json;
    }
}
