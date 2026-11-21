namespace Immons.Tools.Maui.Inspector.Features.SignalR;

/// <summary>Connection state of one inspected hub, as the panel shows it.</summary>
internal enum SignalRState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>Which way a hub message travelled; <see cref="None"/> for lifecycle events.</summary>
internal enum SignalRDirection
{
    None,
    /// <summary>Client → server (↑).</summary>
    Out,
    /// <summary>Server → client (↓).</summary>
    In,
}

/// <summary>Event kinds — strings on the wire so the panel needs no enum table.</summary>
internal static class SignalRKinds
{
    public const string Invocation = "invocation";
    public const string StreamInvocation = "stream-invocation";
    public const string StreamItem = "stream-item";
    public const string Completion = "completion";
    public const string CancelInvocation = "cancel";
    public const string Ping = "ping";
    public const string Close = "close";
    public const string Ack = "ack";
    public const string Sequence = "sequence";
    public const string State = "state";
    public const string Retry = "retry";
    public const string Error = "error";
    public const string Other = "other";
}

/// <summary>
/// One entry of a hub's timeline: a protocol message in either direction or a lifecycle change.
/// <see cref="Payload"/> is already masked and cut to the configured size — the buffer never
/// holds the original arguments.
/// </summary>
internal sealed record SignalREvent
{
    public long Seq { get; init; }

    public required string Hub { get; init; }

    public DateTimeOffset Time { get; init; }

    public SignalRDirection Direction { get; init; }

    public required string Kind { get; init; }

    /// <summary>Hub method (invocations) or the method a completion/stream item answers.</summary>
    public string? Method { get; init; }

    public string? InvocationId { get; init; }

    /// <summary>Encoded size on the wire, protocol framing included; 0 for lifecycle events.</summary>
    public int Bytes { get; init; }

    /// <summary>Masked, truncated JSON (or text) of the message; null when nothing to show.</summary>
    public string? Payload { get; init; }

    public bool Truncated { get; init; }

    public string? Error { get; init; }

    /// <summary>Invoke → completion time for completions; null otherwise.</summary>
    public double? DurationMs { get; init; }

    /// <summary>New state for <see cref="SignalRKinds.State"/> events ("Connected", "Reconnecting"…).</summary>
    public string? State { get; init; }

    /// <summary>Reconnect attempt number for retry/state events.</summary>
    public int? Attempt { get; init; }

    /// <summary>Delivered from the panel ("inject incoming message"), not by the server.</summary>
    public bool Injected { get; init; }

    public string? ConnectionId { get; init; }
}
