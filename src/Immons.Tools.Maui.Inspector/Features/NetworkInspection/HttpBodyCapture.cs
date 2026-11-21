using System.Text;

namespace Immons.Tools.Maui.Inspector.Features.NetworkInspection;

/// <summary>How a body gets into the Network tab without costing the call anything.</summary>
internal enum CapturePlan
{
    /// <summary>Nothing to capture: no body, binary, untyped, or known to be over the cap.</summary>
    None,

    /// <summary>Textual and known to fit under the cap — buffering it is cheap and keeps it resendable.</summary>
    Buffer,

    /// <summary>Textual but of unknown length (chunked, a stream): copied while the transport reads it,
    /// never buffered ahead of the transfer.</summary>
    Tee,
}

/// <summary>Safe reading and replacing of HTTP bodies (textual content only, capped size).</summary>
internal static class HttpBodyCapture
{
    static readonly string[] TextualHints = ["json", "text", "xml", "x-www-form-urlencoded", "javascript", "graphql"];

    internal static int MaxBytes => MauiInspector.Options.MaxCapturedBodyBytes;

    /// <summary>
    /// Decides from the headers alone — reading a body to find out how big it is would be exactly the
    /// buffering this avoids. No Content-Type counts as binary (a file upload, <c>ByteArrayContent</c>);
    /// a known length over the cap is not read at all.
    /// </summary>
    public static CapturePlan PlanFor(HttpContent? content, int maxBytes)
    {
        if (content == null || maxBytes <= 0)
            return CapturePlan.None;
        try
        {
            var mediaType = content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.Length == 0 || !TextualHints.Any(h => mediaType.Contains(h, StringComparison.OrdinalIgnoreCase)))
                return CapturePlan.None;
            return content.Headers.ContentLength switch
            {
                null => CapturePlan.Tee,
                var length when length <= maxBytes => CapturePlan.Buffer,
                _ => CapturePlan.None,
            };
        }
        catch
        {
            return CapturePlan.None;
        }
    }

    /// <summary>Buffered body as string — only for <see cref="CapturePlan.Buffer"/> content, whose
    /// size is known to be under the cap. Content stays resendable.</summary>
    public static async Task<string?> ReadBufferedAsync(HttpContent content, int maxBytes)
    {
        try
        {
            await content.LoadIntoBufferAsync(maxBytes).ConfigureAwait(false);
            return await content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return null; // unreadable body must never break the actual call
        }
    }

    /// <summary>
    /// For breakpoints, which must show a <see cref="CapturePlan.Tee"/> body before it moves: reads at
    /// most <paramref name="maxBytes"/> + 1 bytes. Small → the content is replaced by those bytes and
    /// the text returned; bigger → the content is replaced by "what was read + the rest of the stream"
    /// and null returned. Either way at most the cap sits in memory.
    /// </summary>
    public static async Task<(HttpContent Content, string? Body)> PeekAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var prefix = new byte[maxBytes + 1];
        var filled = 0;
        while (filled < prefix.Length)
        {
            var read = await source.ReadAsync(prefix.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            filled += read;
        }

        if (filled <= maxBytes)
        {
            await source.DisposeAsync().ConfigureAwait(false);
            var small = new ByteArrayContent(prefix, 0, filled);
            CopyHeaders(content, small);
            return (small, Encoding.UTF8.GetString(prefix, 0, filled));
        }

        var rest = new StreamContent(new PrefixedStream(prefix, filled, source));
        CopyHeaders(content, rest);
        return (rest, null);
    }

    /// <summary>New content with the given body, preserving the original media type (default JSON).</summary>
    public static StringContent Replace(HttpContent? original, string body)
    {
        var mediaType = original?.Headers.ContentType?.MediaType ?? "application/json";
        return new StringContent(body, Encoding.UTF8, mediaType);
    }

    static void CopyHeaders(HttpContent from, HttpContent to)
    {
        foreach (var header in from.Headers)
        {
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                to.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>Bytes already read, then the rest of the original stream.</summary>
    sealed class PrefixedStream(byte[] prefix, int prefixLength, Stream rest) : Stream
    {
        int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_offset < prefixLength)
            {
                var n = Math.Min(buffer.Length, prefixLength - _offset);
                prefix.AsSpan(_offset, n).CopyTo(buffer);
                _offset += n;
                return n;
            }
            return rest.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset < prefixLength)
                return Read(buffer.Span);
            return await rest.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                rest.Dispose();
            base.Dispose(disposing);
        }
    }
}
