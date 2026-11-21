using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>
/// Decorator over the hub's real protocol (JSON, Newtonsoft, MessagePack…). Every call goes
/// straight to the inner protocol, which alone reads and writes the bytes — the decorator only
/// looks at the parsed message and the frame it came from, so nothing on the wire changes.
/// </summary>
internal sealed class InspectingHubProtocol(IHubProtocol inner, HubSession session) : IHubProtocol
{
    public IHubProtocol Inner => inner;

    public string Name => inner.Name;

    public int Version => inner.Version;

    public TransferFormat TransferFormat => inner.TransferFormat;

    public bool IsVersionSupported(int version) => inner.IsVersionSupported(version);

    public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, [NotNullWhen(true)] out HubMessage? message)
    {
        var before = input;
        bool parsed;
        try
        {
            parsed = inner.TryParseMessage(ref input, binder, out message);
        }
        catch (Exception ex)
        {
            session.OnParseError(ex, before.Length);
            throw;
        }

        if (parsed && message != null)
            session.OnIncoming(message, before.Slice(before.Start, input.Start));
        return parsed;
    }

    public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
    {
        var capture = new CapturingBufferWriter(output, session.CaptureLimit);
        inner.WriteMessage(message, capture);
        session.OnOutgoing(message, capture.Captured, capture.Written);
    }

    public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
    {
        var bytes = inner.GetMessageBytes(message);
        session.OnOutgoing(message, bytes.Span, bytes.Length);
        return bytes;
    }
}

/// <summary>
/// Passes writes through to the real output and keeps a copy of the first
/// <c>limit</c> bytes for the preview. The bytes the inner protocol writes land in the real
/// buffer unchanged; the copy is taken just before each <see cref="Advance"/>.
/// </summary>
internal sealed class CapturingBufferWriter(IBufferWriter<byte> output, int limit) : IBufferWriter<byte>
{
    Memory<byte> _current;
    byte[]? _copy;
    int _copied;

    public int Written { get; private set; }

    public ReadOnlySpan<byte> Captured => _copy == null ? default : _copy.AsSpan(0, _copied);

    public void Advance(int count)
    {
        if (limit > 0 && _copied < limit && count > 0 && count <= _current.Length)
        {
            var take = Math.Min(count, limit - _copied);
            if (_copy == null || _copy.Length < _copied + take)
                Array.Resize(ref _copy, Math.Min(limit, Math.Max((_copied + take) * 2, 256)));
            _current.Span[..take].CopyTo(_copy.AsSpan(_copied));
            _copied += take;
        }
        Written += count;
        _current = default;
        output.Advance(count);
    }

    public Memory<byte> GetMemory(int sizeHint = 0) => _current = output.GetMemory(sizeHint);

    // Span callers get the same memory, so Advance can still see what was written.
    public Span<byte> GetSpan(int sizeHint = 0) => (_current = output.GetMemory(sizeHint)).Span;
}
