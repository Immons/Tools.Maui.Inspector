using System.Net;
using System.Text;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>
/// Watches a body while the transport moves it: counts the bytes (upload/download progress for the
/// Network tab) and keeps a copy of at most <c>captureLimit</c> bytes. Nothing is buffered ahead —
/// the bytes flow through as the handler reads or writes them.
/// </summary>
internal sealed class BodyObserver(int captureLimit, Action<long>? progress = null, Action<string?>? captured = null)
{
    readonly object _gate = new();
    readonly MemoryStream? _copy = captureLimit > 0 ? new MemoryStream() : null;
    long _bytes;
    bool _overflow;
    bool _done;

    public long Bytes => Interlocked.Read(ref _bytes);

    public void Observe(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return;
        var total = Interlocked.Add(ref _bytes, data.Length);
        if (_copy != null)
        {
            lock (_gate)
            {
                if (!_overflow && !_done)
                {
                    if (_copy.Length + data.Length > captureLimit)
                    {
                        _overflow = true;
                        _copy.SetLength(0); // over the cap: the body is dropped, like a known-large one
                    }
                    else
                    {
                        _copy.Write(data);
                    }
                }
            }
        }
        progress?.Invoke(total);
    }

    /// <summary>A body that restarts from the beginning (redirect, auth retry) — count and copy anew.</summary>
    public void Rewind()
    {
        Interlocked.Exchange(ref _bytes, 0);
        lock (_gate)
        {
            if (_done)
                return; // the copy was already reported (and released)
            _copy?.SetLength(0);
            _overflow = false;
        }
        progress?.Invoke(0);
    }

    /// <summary>End of the body. <paramref name="complete"/> false = the reader stopped early, so the
    /// copy is partial and is not reported.</summary>
    public void Finish(bool complete)
    {
        string? text = null;
        lock (_gate)
        {
            if (_done)
                return;
            _done = true;
            if (_copy != null && complete && !_overflow)
                text = Encoding.UTF8.GetString(_copy.GetBuffer(), 0, (int)_copy.Length);
            _copy?.Dispose();
        }
        if (_copy != null)
            captured?.Invoke(text);
    }
}

/// <summary>
/// Wraps a request or response body so <see cref="BodyObserver"/> sees it in transit. Both ways a
/// handler can consume content stay streaming: <see cref="SerializeToStreamAsync(Stream, TransportContext?, CancellationToken)"/>
/// (SocketsHttpHandler, Android) lets the inner content write through a counting stream, and
/// <see cref="CreateContentReadStreamAsync(CancellationToken)"/> (NSUrlSessionHandler reads
/// <c>ReadAsStreamAsync</c>) hands out the inner stream wrapped — the base class would buffer the
/// whole body there first, which for a 300 MB upload means 300 MB of memory before the first byte leaves.
/// </summary>
internal sealed class ObservedContent : HttpContent
{
    readonly HttpContent _inner;
    readonly BodyObserver _observer;

    public ObservedContent(HttpContent inner, BodyObserver observer)
    {
        _inner = inner;
        _observer = observer;
        foreach (var header in inner.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    public HttpContent Inner => _inner;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        _observer.Rewind(); // a redirect or auth retry sends the body again from the start
        var target = new ObservingStream(stream, _observer, writing: true);
        var complete = false;
        try
        {
            await _inner.CopyToAsync(target, context, cancellationToken).ConfigureAwait(false);
            complete = true;
        }
        finally
        {
            _observer.Finish(complete);
        }
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        _observer.Rewind(); // a redirect or auth retry sends the body again from the start
        var target = new ObservingStream(stream, _observer, writing: true);
        var complete = false;
        try
        {
            _inner.CopyTo(target, context, cancellationToken);
            complete = true;
        }
        finally
        {
            _observer.Finish(complete);
        }
    }

    protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        new ObservingStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _observer, writing: false);

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
        new ObservingStream(_inner.ReadAsStream(cancellationToken), _observer, writing: false);

    protected override bool TryComputeLength(out long length)
    {
        var known = _inner.Headers.ContentLength;
        length = known ?? 0;
        return known != null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Pass-through stream reporting every byte read from (or written to) it.</summary>
internal sealed class ObservingStream(Stream inner, BodyObserver observer, bool writing) : Stream
{
    bool _eof;

    public override bool CanRead => !writing && inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => writing && inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            inner.Position = value;
            if (value == 0)
                observer.Rewind();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        AfterRead(buffer[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        AfterRead(buffer.Span[..read]);
        return read;
    }

    void AfterRead(ReadOnlySpan<byte> data)
    {
        if (data.Length > 0)
        {
            observer.Observe(data);
        }
        else if (!_eof)
        {
            _eof = true;
            observer.Finish(complete: true);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        observer.Observe(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        observer.Observe(buffer.Span);
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = inner.Seek(offset, origin);
        if (position == 0)
            observer.Rewind();
        return position;
    }

    public override void SetLength(long value) => inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A reader that quits before the end leaves a partial copy — reported as "not captured".
            if (!writing)
                observer.Finish(complete: _eof);
            // The write side wraps the transport's own stream: never close it from here.
            if (!writing)
                inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
