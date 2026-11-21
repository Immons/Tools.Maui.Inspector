using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Immons.Tools.Maui.Inspector.Features.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>What the timeline needs from a parsed hub message.</summary>
internal readonly record struct MessageInfo(string Kind, string? Method, string? InvocationId, string? Error);

internal static class MessageDescriber
{
    public static MessageInfo Describe(HubMessage message) => message switch
    {
        InvocationBindingFailureMessage f => new(SignalRKinds.Invocation, f.Target, f.InvocationId,
            "binding failed: " + f.BindingFailure.SourceException.Message),
        StreamBindingFailureMessage f => new(SignalRKinds.StreamItem, null, f.Id,
            "binding failed: " + f.BindingFailure.SourceException.Message),
        StreamInvocationMessage s => new(SignalRKinds.StreamInvocation, s.Target, s.InvocationId, null),
        InvocationMessage i => new(SignalRKinds.Invocation, i.Target, i.InvocationId, null),
        StreamItemMessage s => new(SignalRKinds.StreamItem, null, s.InvocationId, null),
        CompletionMessage c => new(SignalRKinds.Completion, null, c.InvocationId, c.Error),
        CancelInvocationMessage c => new(SignalRKinds.CancelInvocation, null, c.InvocationId, null),
        PingMessage => new(SignalRKinds.Ping, null, null, null),
        CloseMessage c => new(SignalRKinds.Close, null, null, c.Error),
        AckMessage => new(SignalRKinds.Ack, null, null, null),
        SequenceMessage => new(SignalRKinds.Sequence, null, null, null),
        _ => new(SignalRKinds.Other, message.GetType().Name, null, null),
    };

    static readonly JsonSerializerOptions ObjectOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// The message as JSON for protocols whose bytes are not JSON (MessagePack): built from the
    /// .NET objects the protocol produced or was given, so it shows values, not wire bytes.
    /// </summary>
    public static JsonNode? ObjectPayload(HubMessage message) => message switch
    {
        StreamInvocationMessage s => Invocation(s.Target, s.InvocationId, s.Arguments, s.StreamIds),
        InvocationMessage i => Invocation(i.Target, i.InvocationId, i.Arguments, i.StreamIds),
        StreamItemMessage s => new JsonObject { ["invocationId"] = s.InvocationId, ["item"] = ToNode(s.Item) },
        CompletionMessage c => Completion(c),
        CancelInvocationMessage c => new JsonObject { ["invocationId"] = c.InvocationId },
        CloseMessage c => new JsonObject { ["error"] = c.Error, ["allowReconnect"] = c.AllowReconnect },
        AckMessage a => new JsonObject { ["sequenceId"] = a.SequenceId },
        SequenceMessage s => new JsonObject { ["sequenceId"] = s.SequenceId },
        _ => null,
    };

    static JsonObject Invocation(string target, string? invocationId, object?[] arguments, string[]? streamIds)
    {
        var args = new JsonArray();
        foreach (var argument in arguments)
            args.Add(ToNode(argument));
        var json = new JsonObject { ["target"] = target };
        if (invocationId != null)
            json["invocationId"] = invocationId;
        json["arguments"] = args;
        if (streamIds is { Length: > 0 })
            json["streamIds"] = new JsonArray(streamIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        return json;
    }

    static JsonObject Completion(CompletionMessage c)
    {
        var json = new JsonObject { ["invocationId"] = c.InvocationId };
        if (c.HasResult)
            json["result"] = ToNode(c.Result);
        if (c.Error != null)
            json["error"] = c.Error;
        return json;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Debug-only preview; failures fall back to the type name.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Debug-only preview; failures fall back to the type name.")]
    static JsonNode? ToNode(object? value)
    {
        if (value == null)
            return null;
        try
        {
            return JsonSerializer.SerializeToNode(value, value.GetType(), ObjectOptions);
        }
        catch (Exception)
        {
            return JsonValue.Create($"<{value.GetType().Name}>");
        }
    }
}
