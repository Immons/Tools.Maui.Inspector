using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Immons.Tools.Maui.Inspector.Web.Hosting;
using Immons.Tools.Maui.Inspector.Web.Http;

var failures = 0;

// Everything a crash reporter would see: SessionReplay & co. hook exactly this event.
var unobserved = new ConcurrentQueue<Exception>();
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    unobserved.Enqueue(e.Exception);
    e.SetObserved();
};

var big = new byte[32 * 1024 * 1024];
Array.Fill(big, (byte)'x');

// ── Requests and responses ──────────────────────────────────────────────────────────────
await using (var server = TestServer.Start())
{
    using var http = Client();
    var first = await http.GetAsync(server.Url("/echo?a=1&b=two%20words&c=%C5%BC"));
    var body = await first.Content.ReadAsStringAsync();
    Check("GET is dispatched with method, path and decoded query", first.IsSuccessStatusCode && body == "GET /echo a=1 b=two words c=ż");
    Check("…with the response headers of HttpResponse", first.Headers.CacheControl?.NoStore == true
        && first.Headers.TryGetValues("Access-Control-Allow-Origin", out var origin) && origin.Single() == "*");
    Check("…and Content-Type / Content-Length", first.Content.Headers.ContentType?.MediaType == "text/plain"
        && first.Content.Headers.ContentLength == Encoding.UTF8.GetByteCount(body));

    var second = await http.GetStringAsync(server.Url("/echo?x=2"));
    Check("keep-alive: the next request reuses the connection", second == "GET /echo x=2" && server.Listener.ConnectionCount == 1);

    var json = await http.PostAsync(server.Url("/json"), new StringContent("{\"on\":true,\"name\":\"ąę\"}", Encoding.UTF8, "application/json"));
    Check("POST body with Content-Length reaches RequestBody.ReadJson", await json.Content.ReadAsStringAsync() == "true ąę");

    var payload = Enumerable.Range(0, 200_000).Select(i => (byte)('a' + i % 26)).ToArray();
    var chunked = new StreamContent(new NonSeekableStream(payload));
    var chunkedResponse = await http.PostAsync(server.Url("/length"), chunked);
    Check("chunked request body is decoded", await chunkedResponse.Content.ReadAsStringAsync() == payload.Length.ToString());

    using var expect = new HttpRequestMessage(HttpMethod.Post, server.Url("/length")) { Content = new ByteArrayContent(payload) };
    expect.Headers.ExpectContinue = true;
    var expectResponse = await http.SendAsync(expect);
    Check("Expect: 100-continue is answered", await expectResponse.Content.ReadAsStringAsync() == payload.Length.ToString());

    var large = await http.GetByteArrayAsync(server.Url("/big"));
    Check("a 32 MB response arrives whole", large.Length == big.Length && large[^1] == (byte)'x');

    using var head = new HttpRequestMessage(HttpMethod.Head, server.Url("/echo"));
    var headResponse = await http.SendAsync(head);
    Check("HEAD gets headers without a body", headResponse.IsSuccessStatusCode && (await headResponse.Content.ReadAsByteArrayAsync()).Length == 0);

    var closed = false;
    try
    {
        await http.GetAsync(server.Url("/nothing")).WaitAsync(TimeSpan.FromSeconds(10));
    }
    catch (HttpRequestException)
    {
        closed = true;
    }
    Check("an exchange the handler never answered is closed, not left hanging", closed);

    Check("raw: Connection: close is honoured", await Raw.ClosesAfterResponse(server.Port, "GET /echo HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n"));
    Check("raw: malformed request line → 400", (await Raw.StatusOf(server.Port, "NONSENSE\r\n\r\n")) == 400);
    Check("raw: oversized head → 431", (await Raw.StatusOf(server.Port, "GET / HTTP/1.1\r\nX: " + new string('a', 40_000) + "\r\n\r\n")) == 431);
    Check("raw: HTTP/1.0 without Host works", (await Raw.StatusOf(server.Port, "GET /echo HTTP/1.0\r\n\r\n")) == 200);
    Check("no warnings for well-formed traffic", server.Warnings.IsEmpty, string.Join(" | ", server.Warnings));
}

// ── Clients that vanish ─────────────────────────────────────────────────────────────────
await using (var server = TestServer.Start())
{
    // Reads the start of the 32 MB answer, then resets the connection (closed tab, dead Wi-Fi).
    await Raw.ResetAfterReading(server.Port, "GET /big HTTP/1.1\r\nHost: x\r\n\r\n", 64 * 1024);
    var result = await server.NextSendResult(TimeSpan.FromSeconds(20));
    Check("reset in the middle of a large response: the write reports false", result == false);
    Check("…without an exception in the handler", server.HandlerErrors.IsEmpty, string.Join(" | ", server.HandlerErrors));

    // Long poll answered after the client gave up (the panel's waits, maui-inspector-sync polls).
    // (the pause lets the server read the request first — a reset discards unread data)
    await Raw.ResetAfterReading(server.Port, "GET /slow HTTP/1.1\r\nHost: x\r\n\r\n", 0, lingerMs: 100);
    Check("long-poll answered after the client left: false, no exception", await server.NextSendResult(TimeSpan.FromSeconds(10)) == false
        && server.HandlerErrors.IsEmpty);

    // Streaming-style: many writes in a row to a reader that stopped and vanished.
    await Raw.ResetAfterReading(server.Port, "GET /stream HTTP/1.1\r\nHost: x\r\n\r\n", 1024);
    Check("a sequence of writes after a reset never throws", await server.NextSendResult(TimeSpan.FromSeconds(10)) is not null
        && server.HandlerErrors.IsEmpty);

    // ~200 disconnects of every shape, 8 at a time.
    var modes = Enum.GetValues<Raw.Abort>();
    var storm = 0;
    for (var batch = 0; batch < 25; batch++)
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Raw.Run(server.Port, modes[(batch * 8 + i) % modes.Length])));
        storm += 8;
    }
    await server.Quiesce(TimeSpan.FromSeconds(30));
    using var http = Client();
    Check($"after {storm} aborted exchanges the server still answers", await http.GetStringAsync(server.Url("/echo?ok=1")) == "GET /echo ok=1");
    Check("…no handler saw an exception", server.HandlerErrors.IsEmpty, string.Join(" | ", server.HandlerErrors.Take(3)));
    Check("…and nothing was logged as a warning (disconnects are Debug)", server.Warnings.IsEmpty, string.Join(" | ", server.Warnings.Take(3)));
}

// ── Shutdown with requests in flight ────────────────────────────────────────────────────
{
    var server = TestServer.Start();
    var clients = Enumerable.Range(0, 6).Select(_ => Raw.Run(server.Port, Raw.Abort.None, "/slowbig"))
        .Concat(Enumerable.Range(0, 16).Select(_ => Raw.Run(server.Port, Raw.Abort.SlowReader))).ToArray();
    await Task.Delay(400);
    var pendingAccept = server.Listener.AcceptAsync();
    server.Listener.Stop();
    Exception? acceptError = null;
    try
    {
        await pendingAccept.WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (Exception ex)
    {
        acceptError = ex;
    }
    Check("Stop faults a pending AcceptAsync", acceptError != null && acceptError is not TimeoutException);
    Check("…and IsListening goes false", !server.Listener.IsListening);
    await Task.WhenAll(clients);
    await server.Quiesce(TimeSpan.FromSeconds(10));
    Check("writes cut by Stop end quietly", server.HandlerErrors.IsEmpty && server.Warnings.IsEmpty,
        string.Join(" | ", server.HandlerErrors.Concat<object>(server.Warnings).Take(3)));
    Check("…and every connection is closed", server.Listener.ConnectionCount == 0);
    await server.DisposeAsync();
}

// ── Under the supervisor: bind, verify, rebuild on the same port ─────────────────────────
{
    var port = FreePort();
    var dispatched = 0;
    var supervisor = new ListenerSupervisor<InspectorHttpContext>(
        p => new InspectorHttpListener(p),
        async p =>
        {
            using var probe = Client();
            try
            {
                return await probe.GetStringAsync($"http://127.0.0.1:{p}/ping") == "pong" ? ProbeResult.Success : ProbeResult.Failed("wrong answer");
            }
            catch (Exception ex)
            {
                return ProbeResult.Failed(ex.Message);
            }
        },
        context =>
        {
            Interlocked.Increment(ref dispatched);
            _ = Task.Run(async () =>
            {
                await HttpResponse.WriteText(context, 200, "pong");
                context.Response.Close();
            });
        },
        (_, _) => { });
    Check("supervisor starts and verifies the real listener", await supervisor.TryStartAsync(port) == null && supervisor.IsRunning);
    Check("…rebuilds it on the same port", await supervisor.RestartAsync("test") && supervisor.Port == port && supervisor.Restarts == 1);
    using var http = Client();
    Check("…which serves afterwards", await http.GetStringAsync($"http://127.0.0.1:{port}/ping") == "pong");
    supervisor.Stop();
}

// ── Nothing unobserved ──────────────────────────────────────────────────────────────────
await DrainFinalizers();
Check("no TaskScheduler.UnobservedTaskException from the server", unobserved.IsEmpty,
    string.Join(" | ", unobserved.Take(3).Select(e => e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message)));

// ── Baseline: the same storm against System.Net.HttpListener (informational) ─────────────
// Kept to show what 0.10.1 and older did; never fails the run.
{
    var before = unobserved.Count;
    var count = await Baseline.Run(big);
    await DrainFinalizers();
    Console.WriteLine($"INFO  System.Net.HttpListener baseline: {count} aborted exchanges → {unobserved.Count - before} unobserved task exception(s)"
        + (unobserved.Skip(before).FirstOrDefault() is { } sample ? $" (e.g. {sample.GetBaseException().GetType().Name}: {sample.GetBaseException().Message})" : ""));
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
    if (!ok)
        failures++;
}

static HttpClient Client() => new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };

static int FreePort()
{
    using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    return ((IPEndPoint)probe.LocalEndPoint!).Port;
}

static async Task DrainFinalizers()
{
    for (var i = 0; i < 3; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(200);
    }
}

/// <summary>The inspector's dispatch in miniature: accept, run the handler, always end the exchange.</summary>
sealed class TestServer : IAsyncDisposable
{
    readonly Task _loop;
    int _inFlight;

    TestServer()
    {
        var trace = Environment.GetEnvironmentVariable("HTTP_TRACE") == "1";
        Listener = new InspectorHttpListener(0, new HttpServerLog((_, m) => Warnings.Enqueue(m),
            m => { if (trace) Console.WriteLine($"  debug {DateTime.Now:HH:mm:ss.fff} {m}"); }));
        _loop = Task.Run(Loop);
    }

    public InspectorHttpListener Listener { get; }

    public int Port => Listener.Port;

    public ConcurrentQueue<Exception> HandlerErrors { get; } = new();

    public ConcurrentQueue<string> Warnings { get; } = new();

    readonly System.Threading.Channels.Channel<bool> _sendResults = System.Threading.Channels.Channel.CreateUnbounded<bool>();

    public static TestServer Start() => new();

    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

    public async Task<bool?> NextSendResult(TimeSpan timeout)
    {
        try
        {
            return await _sendResults.Reader.ReadAsync().AsTask().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Waits until no handler runs any more.</summary>
    public async Task Quiesce(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _inFlight) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        await Task.Delay(200);
    }

    async Task Loop()
    {
        while (true)
        {
            InspectorHttpContext context;
            try
            {
                context = await Listener.AcceptAsync();
            }
            catch
            {
                return;
            }
            Interlocked.Increment(ref _inFlight);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Handle(context);
                }
                catch (Exception ex)
                {
                    HandlerErrors.Enqueue(ex);
                }
                finally
                {
                    context.Response.Close();
                    Interlocked.Decrement(ref _inFlight);
                }
            });
        }
    }

    static readonly byte[] Big = CreateBig();

    static byte[] CreateBig()
    {
        var bytes = new byte[32 * 1024 * 1024];
        Array.Fill(bytes, (byte)'x');
        return bytes;
    }

    async Task Handle(InspectorHttpContext context)
    {
        var path = context.Request.Url.AbsolutePath;
        switch (path)
        {
            case "/echo":
                var query = context.Request.QueryString;
                var pairs = query.AllKeys.Select(k => $"{k}={query[k]}");
                await HttpResponse.WriteText(context, 200, $"{context.Request.HttpMethod} {path}{(query.Count > 0 ? " " : "")}{string.Join(" ", pairs)}");
                break;
            case "/ping":
                await HttpResponse.WriteText(context, 200, "pong");
                break;
            case "/json":
                var node = await RequestBody.ReadJson(context);
                await HttpResponse.WriteText(context, 200, $"{(bool)node!["on"]!} {(string?)node["name"]}".Replace("True", "true"));
                break;
            case "/length":
                var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);
                await HttpResponse.WriteText(context, 200, buffer.Length.ToString());
                break;
            case "/big":
                _sendResults.Writer.TryWrite(await HttpResponse.WriteBytes(context, "application/octet-stream", Big));
                break;
            case "/slow":
                await Task.Delay(300);
                _sendResults.Writer.TryWrite(await HttpResponse.WriteJson(context, "{\"late\":true}"));
                break;
            case "/slowbig":
                await Task.Delay(300);
                await HttpResponse.WriteBytes(context, "application/octet-stream", Big);
                break;
            case "/stream":
                var last = true;
                for (var i = 0; i < 50; i++)
                    last = await HttpResponse.WriteBytes(context, "application/octet-stream", Big) && last;
                _sendResults.Writer.TryWrite(last);
                break;
            default:
                return; // nothing sent: the dispatch loop's Close() ends the exchange
        }
    }

    public async ValueTask DisposeAsync()
    {
        Listener.Stop();
        await _loop.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

static class Raw
{
    public enum Abort
    {
        None,
        ResetBeforeResponse,
        ResetMidResponse,
        CloseMidResponse,
        ResetMidHead,
        ResetMidBody,
        ConnectOnly,
        LongPollGone,
        ClientTimeout,
        SlowReader,
    }

    public static async Task Run(int port, Abort mode, string path = "/big")
    {
        try
        {
            switch (mode)
            {
                case Abort.None:
                    await ResetAfterReading(port, $"GET {path} HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n", int.MaxValue, graceful: true);
                    break;
                case Abort.ResetBeforeResponse:
                    await ResetAfterReading(port, "GET /big HTTP/1.1\r\nHost: x\r\n\r\n", 0);
                    break;
                case Abort.ResetMidResponse:
                    await ResetAfterReading(port, "GET /big HTTP/1.1\r\nHost: x\r\n\r\n", 256 * 1024);
                    break;
                case Abort.CloseMidResponse:
                    await ResetAfterReading(port, "GET /big HTTP/1.1\r\nHost: x\r\n\r\n", 256 * 1024, graceful: true);
                    break;
                case Abort.ResetMidHead:
                    await ResetAfterReading(port, "GET /big HTTP/1.1\r\nHo", 0);
                    break;
                case Abort.ResetMidBody:
                    await ResetAfterReading(port, "POST /length HTTP/1.1\r\nHost: x\r\nContent-Length: 100000\r\n\r\nabc", 0);
                    break;
                case Abort.ConnectOnly:
                    await ResetAfterReading(port, "", 0, graceful: true);
                    break;
                case Abort.LongPollGone:
                    await ResetAfterReading(port, "GET /slow HTTP/1.1\r\nHost: x\r\n\r\n", 0, graceful: true, lingerMs: 100);
                    break;
                case Abort.SlowReader:
                    // Reads a little now and then — the server is mid-write when it shuts down.
                    using (var slow = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        slow.ReceiveBufferSize = 4096;
                        await slow.ConnectAsync(IPAddress.Loopback, port);
                        await slow.SendAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: x\r\n\r\n"));
                        var chunk = new byte[1024];
                        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        while (await slow.ReceiveAsync(chunk, SocketFlags.None, limit.Token) > 0)
                            await Task.Delay(20, limit.Token);
                    }
                    break;
                case Abort.ClientTimeout:
                    // curl --max-time 0.2 on a large answer
                    using (var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(50) })
                        await http.GetByteArrayAsync($"http://127.0.0.1:{port}/big");
                    break;
            }
        }
        catch
        {
            // the client side of an abort is allowed to fail — only the server is under test
        }
    }

    /// <summary>Sends <paramref name="request"/>, reads up to <paramref name="readBytes"/>, then
    /// closes — with RST (linger 0) unless <paramref name="graceful"/>.</summary>
    public static async Task ResetAfterReading(int port, string request, int readBytes, bool graceful = false, int lingerMs = 0)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, port);
        if (request.Length > 0)
            await socket.SendAsync(Encoding.ASCII.GetBytes(request));
        var buffer = new byte[64 * 1024];
        var total = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (total < readBytes)
        {
            var read = await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token);
            if (read == 0)
                break;
            total += read;
        }
        if (lingerMs > 0)
            await Task.Delay(lingerMs);
        if (!graceful)
            socket.LingerState = new LingerOption(true, 0);
        socket.Close();
    }

    public static async Task<int> StatusOf(int port, string request)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, port);
        await socket.SendAsync(Encoding.ASCII.GetBytes(request));
        var buffer = new byte[1024];
        var read = await socket.ReceiveAsync(buffer, SocketFlags.None, new CancellationTokenSource(5000).Token);
        var line = Encoding.ASCII.GetString(buffer, 0, read);
        return line.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) ? int.Parse(line.AsSpan(9, 3)) : -1;
    }

    public static async Task<bool> ClosesAfterResponse(int port, string request)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, port);
        await socket.SendAsync(Encoding.ASCII.GetBytes(request));
        var buffer = new byte[64 * 1024];
        using var timeout = new CancellationTokenSource(5000);
        var text = new StringBuilder();
        while (true)
        {
            var read = await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token);
            if (read == 0)
                return text.ToString().Contains("Connection: close", StringComparison.Ordinal);
            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
    }
}

/// <summary>Forces HttpClient to send a chunked body (no known length).</summary>
sealed class NonSeekableStream(byte[] data) : Stream
{
    readonly MemoryStream _inner = new(data);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 7919));
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>0.10.1's server: HttpListener + OutputStream.WriteAsync + Close, every await caught.</summary>
static class Baseline
{
    public static async Task<int> Run(byte[] big)
    {
        int port;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        listener.Start();
        var loop = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;
                }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (context.Request.Url!.AbsolutePath == "/slow")
                            await Task.Delay(300);
                        context.Response.ContentLength64 = big.Length;
                        await context.Response.OutputStream.WriteAsync(big);
                        context.Response.Close();
                    }
                    catch
                    {
                        try
                        {
                            context.Response.Abort();
                        }
                        catch
                        {
                            // gone
                        }
                    }
                });
            }
        });

        var modes = new[] { Raw.Abort.ResetBeforeResponse, Raw.Abort.ResetMidResponse, Raw.Abort.CloseMidResponse, Raw.Abort.ResetMidHead, Raw.Abort.ResetMidBody, Raw.Abort.LongPollGone };
        var count = 0;
        for (var batch = 0; batch < 25; batch++)
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Raw.Run(port, modes[(batch * 8 + i) % modes.Length])));
            count += 8;
        }
        await Task.Delay(1000);
        // The resume case: the listener is rebuilt while responses are being written.
        var inFlight = Enumerable.Range(0, 16).Select(_ => Raw.Run(port, Raw.Abort.SlowReader)).ToArray();
        await Task.Delay(400);
        listener.Stop();
        listener.Close();
        await Task.WhenAll(inFlight);
        count += inFlight.Length;
        await loop.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
        return count;
    }
}
