using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace Immons.Tools.Maui.Inspector.SignalR;

/// <summary>
/// The hub connection's input reader, passed through untouched except for two things:
/// <list type="bullet">
/// <item>the first consumed bytes are the handshake response — that is when the connection is up;</item>
/// <item>injected frames (the panel's "simulate a server call") are handed to the receive loop
/// between two server frames, never inside one.</item>
/// </list>
/// An injection wakes the pending read with <see cref="PipeReader.CancelPendingRead"/>; that
/// canceled read is swallowed and answered with the injected frame instead. A cancel the hub
/// connection asks for itself (stop, server timeout) always wins and is passed on unchanged.
/// </summary>
internal sealed class InspectingPipeReader(PipeReader inner, HubSession session) : PipeReader
{
    readonly ConcurrentQueue<byte[]> _injected = new();
    int _wakeRequested;
    volatile bool _externalCancel;
    bool _handshakeDone;
    ReadOnlySequence<byte> _last;
    bool _lastInjected;

    public bool HandshakeDone => Volatile.Read(ref _handshakeDone);

    /// <summary>Set when the transport supports stateful reconnect: injecting would break its message counting.</summary>
    public bool StatefulReconnect { get; init; }

    public int PendingInjections => _injected.Count;

    /// <summary>Queues a complete protocol frame to be read as if the server had sent it.</summary>
    public void Inject(byte[] frame)
    {
        _injected.Enqueue(frame);
        Wake();
    }

    void Wake()
    {
        if (Interlocked.CompareExchange(ref _wakeRequested, 1, 0) == 0)
            inner.CancelPendingRead();
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                var ours = Interlocked.Exchange(ref _wakeRequested, 0) == 1;
                if (_externalCancel || !ours)
                {
                    _externalCancel = false;
                    return Hand(result, injected: false);
                }

                // Woken for an injection. Deliver only between frames: nothing buffered, stream still open.
                if (result.Buffer.IsEmpty && !result.IsCompleted)
                {
                    inner.AdvanceTo(result.Buffer.Start);
                    if (_handshakeDone && _injected.TryDequeue(out var frame))
                        return Hand(new ReadResult(new ReadOnlySequence<byte>(frame), false, false), injected: true);
                    continue;
                }

                // Server data is pending: read it normally, retry the injection once it is consumed.
                return Hand(new ReadResult(result.Buffer, false, result.IsCompleted), injected: false);
            }
            return Hand(result, injected: false);
        }
    }

    ReadResult Hand(ReadResult result, bool injected)
    {
        _last = result.Buffer;
        _lastInjected = injected;
        session.InjectedFrameInFlight = injected;
        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        if (!inner.TryRead(out result))
            return false;
        if (result.IsCanceled && !_externalCancel && Interlocked.Exchange(ref _wakeRequested, 0) == 1)
            result = new ReadResult(result.Buffer, false, result.IsCompleted);
        _externalCancel = false;
        Hand(result, injected: false);
        return true;
    }

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (_lastInjected)
        {
            // The injected buffer is ours alone — the inner reader was released before handing it out.
            _lastInjected = false;
            session.InjectedFrameInFlight = false;
            if (!_injected.IsEmpty)
                Wake();
            return;
        }

        if (!_handshakeDone && !consumed.Equals(_last.Start))
        {
            Volatile.Write(ref _handshakeDone, true);
            session.OnHandshakeCompleted(_last.Slice(_last.Start, consumed));
        }

        var drained = consumed.Equals(_last.End);
        inner.AdvanceTo(consumed, examined);
        if (drained && _handshakeDone && !_injected.IsEmpty)
            Wake();
    }

    public override void CancelPendingRead()
    {
        _externalCancel = true;
        inner.CancelPendingRead();
    }

    public override void Complete(Exception? exception = null)
    {
        _injected.Clear();
        inner.Complete(exception);
    }

    public override ValueTask CompleteAsync(Exception? exception = null)
    {
        _injected.Clear();
        return inner.CompleteAsync(exception);
    }
}
