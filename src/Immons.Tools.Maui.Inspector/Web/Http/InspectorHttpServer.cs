using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Web;
using Immons.Tools.Maui.Inspector.Web.Hosting;

namespace Immons.Tools.Maui.Inspector.Web.Http;

// The inspector's own HTTP/1.1 server on a plain socket — it replaces System.Net.HttpListener.
//
// Why: on iOS/Android (and every other Unix) HttpListener is the managed implementation, and its
// writes go through APM glue (BeginWrite/EndWrite on NetworkStream/Socket). When the client is gone
// — a closed tab, a phone that slept, a panel poll queued while the app was suspended — the socket
// send faults, and on several paths nobody ever observes that task: EndWrite throws
// ObjectDisposedException before it reaches Socket.EndSend, and the listener's own error page
// (HttpConnection.SendError → HttpListenerResponse.Close(bytes, false)) throws from its callback on a
// thread-pool continuation. Both surface in the app as TaskScheduler.UnobservedTaskException
// ("Broken pipe", "ObjectDisposed … HttpListenerResponse") — crash reporters record them as unhandled
// errors of the app. None of those paths can be reached from outside HttpListener.
//
// Here every socket operation is awaited directly by the code that started it, and every exchange
// ends in a catch: a client that goes away mid-request or mid-response is a Debug line, never an
// exception that leaves the inspector. MAUI-free on purpose — tests/HttpServerTests drives it with
// real sockets.

/// <summary>One request/response exchange, the counterpart of HttpListenerContext.</summary>
internal sealed class InspectorHttpContext
{
    internal InspectorHttpContext(InspectorHttpRequest request, InspectorHttpResponse response)
    {
        Request = request;
        Response = response;
    }

    public InspectorHttpRequest Request { get; }

    public InspectorHttpResponse Response { get; }
}

internal sealed class InspectorHttpRequest
{
    internal InspectorHttpRequest(string method, Uri url, NameValueCollection headers, byte[] body)
    {
        HttpMethod = method;
        Url = url;
        Headers = headers;
        QueryString = HttpUtility.ParseQueryString(url.Query);
        InputStream = new MemoryStream(body, writable: false);
    }

    public string HttpMethod { get; }

    public Uri Url { get; }

    public NameValueCollection Headers { get; }

    public NameValueCollection QueryString { get; }

    /// <summary>The whole body, read before the request was dispatched.</summary>
    public Stream InputStream { get; }
}

/// <summary>
/// A response is sent in one go — status, headers and the whole body with its Content-Length — by
/// <see cref="SendAsync"/>, which never throws: a client that went away makes it return false.
/// </summary>
internal sealed class InspectorHttpResponse
{
    const int Open = 0, Sending = 1, Done = 2;

    readonly Socket _socket;
    readonly bool _keepAlive;
    readonly bool _headOnly;
    readonly HttpServerLog _log;
    readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _state;

    internal InspectorHttpResponse(Socket socket, bool keepAlive, bool headOnly, HttpServerLog log)
    {
        _socket = socket;
        _keepAlive = keepAlive;
        _headOnly = headOnly;
        _log = log;
    }

    public WebHeaderCollection Headers { get; } = new();

    /// <summary>True once <see cref="SendAsync"/> or <see cref="Close"/> was called.</summary>
    public bool HasStarted => Volatile.Read(ref _state) != Open;

    /// <summary>Completes when the exchange is over; true = the connection may carry the next request.</summary>
    internal Task<bool> Completion => _completion.Task;

    public async Task<bool> SendAsync(int status, string contentType, byte[] body)
    {
        if (Interlocked.CompareExchange(ref _state, Sending, Open) != Open)
        {
            _log.Debug("response already sent — second write ignored");
            return false;
        }

        var keepAlive = _keepAlive && !string.Equals(Headers["Connection"], "close", StringComparison.OrdinalIgnoreCase);
        var sent = false;
        try
        {
            var head = Encoding.Latin1.GetBytes(BuildHead(status, contentType, body.Length, keepAlive));
            using var timeout = new CancellationTokenSource(HttpServerLimits.SendTimeout(head.Length + body.Length));
            if (_headOnly || body.Length == 0)
            {
                await SendAllAsync(_socket, head, timeout.Token).ConfigureAwait(false);
            }
            else if (body.Length <= HttpServerLimits.CoalesceBytes)
            {
                var whole = new byte[head.Length + body.Length];
                head.CopyTo(whole, 0);
                body.CopyTo(whole, head.Length);
                await SendAllAsync(_socket, whole, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                await SendAllAsync(_socket, head, timeout.Token).ConfigureAwait(false);
                await SendAllAsync(_socket, body, timeout.Token).ConfigureAwait(false);
            }
            sent = true;
        }
        catch (Exception ex)
        {
            _log.Failure("response not delivered", ex);
        }
        finally
        {
            Volatile.Write(ref _state, Done);
            _completion.TrySetResult(sent && keepAlive);
        }
        return sent;
    }

    /// <summary>Ends an exchange nothing was sent for (handler bug, shutdown): the connection is closed.</summary>
    public void Close()
    {
        if (Interlocked.CompareExchange(ref _state, Done, Open) == Open)
            _completion.TrySetResult(false);
    }

    string BuildHead(int status, string contentType, int length, bool keepAlive)
    {
        var head = new StringBuilder(256);
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n");
        head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        head.Append("Content-Length: ").Append(length).Append("\r\n");
        head.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n");
        foreach (var name in Headers.AllKeys)
        {
            if (name == null
                || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                continue;
            head.Append(name).Append(": ").Append(Sanitize(Headers[name])).Append("\r\n");
        }
        head.Append("\r\n");
        return head.ToString();
    }

    static string Sanitize(string? value) => (value ?? "").Replace('\r', ' ').Replace('\n', ' ');

    internal static string ReasonPhrase(int status) => status switch
    {
        100 => "Continue",
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        304 => "Not Modified",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        408 => "Request Timeout",
        409 => "Conflict",
        410 => "Gone",
        413 => "Content Too Large",
        422 => "Unprocessable Content",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => status switch
        {
            < 200 => "Informational",
            < 300 => "Success",
            < 400 => "Redirection",
            < 500 => "Client Error",
            _ => "Server Error",
        },
    };

    internal static async Task SendAllAsync(Socket socket, ReadOnlyMemory<byte> data, CancellationToken cancellation)
    {
        while (!data.IsEmpty)
        {
            var sent = await socket.SendAsync(data, SocketFlags.None, cancellation).ConfigureAwait(false);
            if (sent <= 0)
                throw new IOException("the connection was closed while sending");
            data = data[sent..];
        }
    }
}

/// <summary>Where the server reports what happened: client disconnects are Debug, the rest Warning.</summary>
internal sealed class HttpServerLog(Action<ServerLogLevel, string>? sink, Action<string>? debug)
{
    public static readonly HttpServerLog None = new(null, null);

    public void Debug(string message)
    {
        try
        {
            debug?.Invoke(message);
        }
        catch
        {
            // logging must never break the server
        }
    }

    public void Warning(string message)
    {
        try
        {
            sink?.Invoke(ServerLogLevel.Warning, message);
        }
        catch
        {
            // logging must never break the server
        }
    }

    /// <summary>A client that went away is expected traffic; anything else is a real problem.</summary>
    public void Failure(string what, Exception ex)
    {
        if (IsClientGone(ex))
            Debug($"{what}: client gone ({ex.GetType().Name}: {ex.Message})");
        else
            Warning($"{what}: {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>The shapes a dropped connection takes: reset/broken pipe, a socket closed under a
    /// pending operation, a timed-out or cancelled exchange.</summary>
    public static bool IsClientGone(Exception ex) => ex switch
    {
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsClientGone),
        SocketException or IOException or ObjectDisposedException or OperationCanceledException or ChannelClosedException => true,
        _ => false,
    };
}

internal static class HttpServerLimits
{
    public const int MaxHeadBytes = 32 * 1024;
    public const int MaxLineBytes = 8 * 1024;
    public const long MaxBodyBytes = 256L * 1024 * 1024;
    public const int CoalesceBytes = 64 * 1024;
    public static readonly TimeSpan FirstRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan KeepAliveIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Bounds a send to a client that stopped reading: 30 s plus 1 s per 128 KB.</summary>
    public static TimeSpan SendTimeout(long bytes) => TimeSpan.FromSeconds(30 + bytes / (128 * 1024));

    /// <summary>Bounds reading a request body: 30 s plus 1 s per 128 KB.</summary>
    public static TimeSpan BodyTimeout(long bytes) => SendTimeout(bytes);
}

/// <summary>
/// One bound listening socket (IPv4 any address, like HttpListener's <c>http://*:port/</c>). Accepted
/// connections are served on their own tasks; complete requests are queued for
/// <see cref="AcceptAsync"/>. Keep-alive is honoured, one request at a time per connection.
/// </summary>
internal sealed class InspectorHttpListener : IInspectorListener<InspectorHttpContext>
{
    readonly Socket _socket;
    readonly HttpServerLog _log;
    readonly Channel<InspectorHttpContext> _queue =
        Channel.CreateUnbounded<InspectorHttpContext>(new UnboundedChannelOptions { SingleReader = true });
    readonly ConcurrentDictionary<Socket, byte> _connections = new();
    readonly CancellationTokenSource _stop = new();
    int _stopped;
    volatile bool _broken;

    /// <summary>Port 0 binds a free port (tests); <see cref="Port"/> says which.</summary>
    public InspectorHttpListener(int port, HttpServerLog? log = null)
    {
        _log = log ?? HttpServerLog.None;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            _socket.Listen(512);
        }
        catch
        {
            _socket.Dispose();
            throw;
        }
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        // Never faults: every exit of the loop is caught inside.
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    /// <summary>Open connections (tests).</summary>
    internal int ConnectionCount => _connections.Count;

    public bool IsListening => Volatile.Read(ref _stopped) == 0 && !_broken;

    public async Task<InspectorHttpContext> AcceptAsync() =>
        await _queue.Reader.ReadAsync().ConfigureAwait(false);

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;
        _queue.Writer.TryComplete(new ObjectDisposedException(nameof(InspectorHttpListener)));
        try
        {
            _stop.Cancel();
        }
        catch
        {
            // a callback threw — nothing to undo
        }
        CloseQuietly(_socket);
        foreach (var connection in _connections.Keys)
            CloseQuietly(connection);
        // Requests queued but never taken: end them so their connection tasks finish.
        while (_queue.Reader.TryRead(out var pending))
            pending.Response.Close();
    }

    async Task AcceptLoopAsync()
    {
        var failures = 0;
        try
        {
            while (Volatile.Read(ref _stopped) == 0)
            {
                Socket client;
                try
                {
                    client = await _socket.AcceptAsync(_stop.Token).ConfigureAwait(false);
                    failures = 0;
                }
                catch (Exception ex)
                {
                    if (Volatile.Read(ref _stopped) == 1)
                        return;
                    // A peer that resets while still queued fails one accept (ECONNABORTED) — keep
                    // going; a run of failures means the socket itself is broken.
                    if (++failures < 5 && ex is SocketException)
                    {
                        _log.Debug($"accept failed ({failures}x): {ex.Message}");
                        await Task.Delay(50 * failures).ConfigureAwait(false);
                        continue;
                    }
                    Break(ex);
                    return;
                }

                _connections.TryAdd(client, 0);
                if (Volatile.Read(ref _stopped) == 1)
                {
                    _connections.TryRemove(client, out _);
                    CloseQuietly(client);
                    return;
                }
                // Never faults: ServeAsync catches everything.
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception ex)
        {
            Break(ex);
        }
    }

    /// <summary>The accept loop died: report through <see cref="AcceptAsync"/> so the supervisor rebuilds.</summary>
    void Break(Exception ex)
    {
        _broken = true;
        _log.Warning($"accept loop stopped: {ex.GetType().Name}: {ex.Message}");
        _queue.Writer.TryComplete(new IOException("the listening socket failed: " + ex.Message, ex));
    }

    async Task ServeAsync(Socket client)
    {
        try
        {
            try
            {
                client.NoDelay = true;
            }
            catch (SocketException)
            {
                // the peer already reset the connection (EINVAL on Apple platforms) — the read below ends it
            }
            var reader = new HttpRequestReader(client);
            var first = true;
            while (Volatile.Read(ref _stopped) == 0)
            {
                HttpRequestReader.Result result;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                {
                    idle.CancelAfter(first ? HttpServerLimits.FirstRequestTimeout : HttpServerLimits.KeepAliveIdleTimeout);
                    result = await reader.ReadAsync(idle.Token).ConfigureAwait(false);
                }

                if (result.Request is not { } request)
                {
                    if (result.ErrorStatus is { } status)
                        await SendErrorAsync(client, status).ConfigureAwait(false);
                    return; // clean close or unusable request
                }

                var response = new InspectorHttpResponse(client, result.KeepAlive, request.HttpMethod == "HEAD", _log);
                var context = new InspectorHttpContext(request, response);
                if (!_queue.Writer.TryWrite(context))
                    return; // stopped meanwhile

                if (!await response.Completion.ConfigureAwait(false))
                    return;
                first = false;
            }
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _stopped) == 1)
                _log.Debug($"connection closed by shutdown ({ex.GetType().Name})");
            else
                _log.Failure("connection", ex);
        }
        finally
        {
            _connections.TryRemove(client, out _);
            CloseQuietly(client);
        }
    }

    async Task SendErrorAsync(Socket client, int status)
    {
        try
        {
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {InspectorHttpResponse.ReasonPhrase(status)}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await InspectorHttpResponse.SendAllAsync(client, head, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Failure($"error response {status} not delivered", ex);
        }
    }

    static void CloseQuietly(Socket socket)
    {
        try
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // already reset by the peer
        }
        try
        {
            socket.Dispose();
        }
        catch
        {
            // already closed
        }
    }
}

/// <summary>Reads HTTP/1.x requests off one connection; leftover bytes carry over to the next request.</summary>
internal sealed class HttpRequestReader(Socket socket)
{
    byte[] _buffer = new byte[8 * 1024];
    int _start;
    int _end;

    /// <summary>A parsed request, or an error status to answer with (then close), or neither = the peer closed.</summary>
    internal readonly record struct Result(InspectorHttpRequest? Request, bool KeepAlive, int? ErrorStatus)
    {
        public static readonly Result Closed = new(null, false, null);

        public static Result Error(int status) => new(null, false, status);
    }

    int Buffered => _end - _start;

    public async Task<Result> ReadAsync(CancellationToken cancellation)
    {
        // Head: everything up to the blank line.
        int headEnd;
        while (true)
        {
            SkipLeadingBlankLines();
            headEnd = IndexOf("\r\n\r\n"u8, 0);
            if (headEnd >= 0)
                break;
            if (Buffered >= HttpServerLimits.MaxHeadBytes)
                return Result.Error(431);
            if (!await FillAsync(HttpServerLimits.MaxHeadBytes, cancellation).ConfigureAwait(false))
                return Result.Closed; // peer closed (possibly mid-request): nobody to answer
        }

        var head = Encoding.Latin1.GetString(_buffer, _start, headEnd);
        _start += headEnd + 4;

        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || requestLine[0].Length == 0)
            return Result.Error(400);
        var method = requestLine[0];
        var target = requestLine[1];
        var http10 = requestLine[2] == "HTTP/1.0";

        var headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0)
                return Result.Error(400);
            headers.Add(lines[i][..colon].Trim(), lines[i][(colon + 1)..].Trim());
        }

        if (!TryBuildUrl(target, headers["Host"], out var url))
            return Result.Error(400);

        var connection = headers["Connection"] ?? "";
        var keepAlive = http10
            ? connection.Contains("keep-alive", StringComparison.OrdinalIgnoreCase)
            : !connection.Contains("close", StringComparison.OrdinalIgnoreCase);

        byte[] body;
        var chunked = (headers["Transfer-Encoding"] ?? "").Contains("chunked", StringComparison.OrdinalIgnoreCase);
        long length = 0;
        if (!chunked && headers["Content-Length"] is { } declared
            && (!long.TryParse(declared, out length) || length < 0))
            return Result.Error(400);
        if (length > HttpServerLimits.MaxBodyBytes)
            return Result.Error(413);

        if ((chunked || length > 0)
            && (headers["Expect"] ?? "").Contains("100-continue", StringComparison.OrdinalIgnoreCase))
            await InspectorHttpResponse.SendAllAsync(socket, "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), cancellation).ConfigureAwait(false);

        using (var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            bodyTimeout.CancelAfter(HttpServerLimits.BodyTimeout(length));
            if (chunked)
            {
                if (await ReadChunkedAsync(bodyTimeout.Token).ConfigureAwait(false) is not { } decoded)
                    return Result.Error(413);
                body = decoded;
            }
            else
            {
                body = await ReadExactAsync((int)length, bodyTimeout.Token).ConfigureAwait(false);
            }
        }

        return new Result(new InspectorHttpRequest(method, url, headers, body), keepAlive, null);
    }

    static bool TryBuildUrl(string target, string? host, out Uri url)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(target, UriKind.Absolute, out url!);
        if (!target.StartsWith('/'))
        {
            url = null!;
            return false;
        }
        var authority = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        return Uri.TryCreate("http://" + authority + target, UriKind.Absolute, out url!)
            || Uri.TryCreate("http://localhost" + target, UriKind.Absolute, out url!);
    }

    /// <summary>Null when the body grows past the limit.</summary>
    async Task<byte[]?> ReadChunkedAsync(CancellationToken cancellation)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var line = await ReadLineAsync(cancellation).ConfigureAwait(false);
            var semicolon = line.IndexOf(';');
            var hex = (semicolon >= 0 ? line[..semicolon] : line).Trim();
            if (!long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0)
                throw new InvalidDataException("bad chunk size");
            if (size == 0)
            {
                // Trailers until the blank line.
                while ((await ReadLineAsync(cancellation).ConfigureAwait(false)).Length > 0)
                {
                }
                return body.ToArray();
            }
            if (body.Length + size > HttpServerLimits.MaxBodyBytes)
                return null;
            var chunk = await ReadExactAsync((int)size, cancellation).ConfigureAwait(false);
            body.Write(chunk);
            if ((await ReadLineAsync(cancellation).ConfigureAwait(false)).Length != 0)
                throw new InvalidDataException("chunk not followed by CRLF");
        }
    }

    async Task<string> ReadLineAsync(CancellationToken cancellation)
    {
        int end;
        var scanFrom = 0;
        while ((end = IndexOf("\r\n"u8, scanFrom)) < 0)
        {
            if (Buffered >= HttpServerLimits.MaxLineBytes)
                throw new InvalidDataException("line too long");
            scanFrom = Math.Max(0, Buffered - 1);
            if (!await FillAsync(HttpServerLimits.MaxLineBytes, cancellation).ConfigureAwait(false))
                throw new IOException("the connection was closed mid-request");
        }
        var line = Encoding.Latin1.GetString(_buffer, _start, end);
        _start += end + 2;
        return line;
    }

    async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellation)
    {
        var result = new byte[count];
        var copied = Math.Min(count, Buffered);
        Buffer.BlockCopy(_buffer, _start, result, 0, copied);
        _start += copied;
        while (copied < count)
        {
            var read = await socket.ReceiveAsync(result.AsMemory(copied), SocketFlags.None, cancellation).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("the connection was closed mid-body");
            copied += read;
        }
        return result;
    }

    /// <summary>False when the peer closed the connection.</summary>
    async Task<bool> FillAsync(int limit, CancellationToken cancellation)
    {
        if (_start > 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, Buffered);
            _end -= _start;
            _start = 0;
        }
        if (_end == _buffer.Length)
        {
            var grown = new byte[Math.Min(Math.Max(_buffer.Length * 2, 1024), limit + 4)];
            if (grown.Length <= _buffer.Length)
                throw new InvalidDataException("request head too large");
            Buffer.BlockCopy(_buffer, 0, grown, 0, _end);
            _buffer = grown;
        }
        var read = await socket.ReceiveAsync(_buffer.AsMemory(_end), SocketFlags.None, cancellation).ConfigureAwait(false);
        if (read == 0)
            return false;
        _end += read;
        return true;
    }

    void SkipLeadingBlankLines()
    {
        while (Buffered >= 2 && _buffer[_start] == '\r' && _buffer[_start + 1] == '\n')
            _start += 2;
    }

    int IndexOf(ReadOnlySpan<byte> needle, int from) =>
        from >= Buffered ? -1 : _buffer.AsSpan(_start + from, Buffered - from).IndexOf(needle) is var i and >= 0 ? i + from : -1;
}
