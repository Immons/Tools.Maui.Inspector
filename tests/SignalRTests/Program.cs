using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.SignalR;
using Immons.Tools.Maui.Inspector.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var failures = 0;

// ── Masking ───────────────────────────────────────────────────────────────────────
{
    var masker = new PayloadMasker(["iban"]);
    var node = JsonNode.Parse("""
        {"user":"ann","password":"p@ss","Access_Token":"abc","nested":{"Authorization":"Bearer xyz","author":"Tolkien"},
         "credentials":{"key":"k"},"shipping":"DHL","pin":"1234","iban":"PL61","list":[{"apiKey":"q"},"Bearer leaked"],
         "jwt_value":"plain","maybe":"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl","count":3}
        """);
    var masked = masker.MaskNode(node)!.ToJsonString();
    Check("password masked", masked.Contains("\"password\":\"***\"") && !masked.Contains("p@ss"));
    Check("access_token masked (case/underscore-insensitive)", masked.Contains("\"Access_Token\":\"***\""));
    Check("nested Authorization masked", masked.Contains("\"Authorization\":\"***\"") && !masked.Contains("xyz"));
    Check("'author' is not a secret", masked.Contains("\"author\":\"Tolkien\""));
    Check("whole object under a secret key masked", masked.Contains("\"credentials\":\"***\""));
    Check("'shipping' does not trip 'pin'", masked.Contains("\"shipping\":\"DHL\""));
    Check("exact 'pin' masked", masked.Contains("\"pin\":\"***\""));
    Check("custom masked field", masked.Contains("\"iban\":\"***\""));
    Check("secrets inside arrays masked", masked.Contains("\"apiKey\":\"***\"") && !masked.Contains("leaked"));
    Check("JWT-looking value masked under any key", masked.Contains("\"maybe\":\"***\""));
    Check("numbers untouched", masked.Contains("\"count\":3"));
    Check("URL query secret masked", masker.MaskUrl("https://h/hub?id=1&access_token=abc#x") == "https://h/hub?id=1&access_token=***#x");
    Check("URL without query unchanged", masker.MaskUrl("https://h/hub") == "https://h/hub");
    var (cut, truncated) = PayloadMasker.Truncate(new string('a', 5000), 4096);
    Check("truncated to the limit with a marker", truncated && cut.Length == 4097 && cut.EndsWith('…'));
    var (emoji, _) = PayloadMasker.Truncate("ab😀cd", 3);
    Check("truncation never splits a surrogate pair", emoji == "ab…");
    var (frame, _) = masker.FromJsonUtf8(Encoding.UTF8.GetBytes("{\"type\":1,\"target\":\"Login\",\"arguments\":[{\"password\":\"x\"}]}\u001e"), 4096);
    Check("JSON frame: type dropped, record separator ignored, masked",
        frame == "{\"target\":\"Login\",\"arguments\":[{\"password\":\"***\"}]}");
    var (polish, _) = masker.FromJsonUtf8(Encoding.UTF8.GetBytes("{\"type\":1,\"arguments\":[\"zażółć\"]}"), 4096);
    Check("Polish letters stay readable (no \\u escapes)", polish == "{\"arguments\":[\"zażółć\"]}");
    var (bad, _) = masker.FromJsonUtf8("token=abc"u8, 4096);
    Check("unparseable frame is never echoed", bad != null && !bad.Contains("abc"));
}

// ── Ring buffer ───────────────────────────────────────────────────────────────────
{
    var buffer = new SignalREventBuffer(3);
    for (var i = 1; i <= 5; i++)
        buffer.Add(new SignalREvent { Seq = i, Hub = "h", Kind = SignalRKinds.Ping });
    var all = buffer.Since(0);
    Check("buffer keeps only the newest N", buffer.Count == 3 && all.Select(e => e.Seq).SequenceEqual([3L, 4L, 5L]));
    Check("buffer counts overwritten entries", buffer.Dropped == 2);
    Check("Since filters by sequence", buffer.Since(4).Single().Seq == 5);
    Check("Find hits a kept entry and misses a dropped one", buffer.Find(4) != null && buffer.Find(1) == null);
    buffer.Clear();
    Check("Clear empties the buffer", buffer.Count == 0 && buffer.Since(0).Count == 0 && buffer.Dropped == 0);
    var big = new SignalREventBuffer(1000);
    for (var i = 1; i <= 25_000; i++)
        big.Add(new SignalREvent { Seq = i, Hub = "h", Kind = SignalRKinds.Ping });
    Check("1000-event buffer stays at 1000 after 25k events", big.Count == 1000 && big.Since(0)[0].Seq == 24_001);
}

// ── Hub state ─────────────────────────────────────────────────────────────────────
{
    var monitor = new SignalRMonitor();
    var hub = monitor.Hub("state");
    monitor.RecordState(hub, SignalRState.Connecting);
    monitor.RecordState(hub, SignalRState.Connected, "c1");
    Check("connected with id", hub.State == SignalRState.Connected && hub.ConnectionId == "c1" && hub.Reconnects == 0);
    monitor.RecordState(hub, SignalRState.Reconnecting, error: "socket closed");
    Check("reconnecting counts a reconnect and keeps the error", hub.Reconnects == 1 && hub.LastError == "socket closed");
    hub.SetState(SignalRState.Reconnecting, null, null, attempt: 3);
    Check("attempt tracked", hub.Attempt == 3 && hub.Reconnects == 1);
    monitor.RecordState(hub, SignalRState.Connected, "c2", note: "Reconnected");
    Check("reconnected resets attempt and error", hub.Attempt == 0 && hub.LastError == null && hub.ConnectionId == "c2" && hub.Reconnects == 1);
    monitor.RecordState(hub, SignalRState.Disconnected, error: "boom", unexpectedLoss: true);
    monitor.RecordState(hub, SignalRState.Connecting);
    monitor.RecordState(hub, SignalRState.Disconnected, error: "refused", note: "Start failed");
    monitor.RecordState(hub, SignalRState.Connecting);
    monitor.RecordState(hub, SignalRState.Connected, "c3");
    Check("app-driven restart after a drop counts as a reconnect (failed starts in between)", hub.Reconnects == 2);
    monitor.RecordState(hub, SignalRState.Disconnected, unexpectedLoss: false, note: "Closed");
    monitor.RecordState(hub, SignalRState.Connected, "c4");
    Check("restart after a deliberate stop is not a reconnect", hub.Reconnects == 2);
    Check("state events are on the timeline", hub.Events.Since(0).All(e => e.Kind == SignalRKinds.State) && hub.Events.Count >= 9);

    monitor.Paused = true;
    var skipped = monitor.Record(hub, new SignalREvent { Hub = "state", Kind = SignalRKinds.Invocation, Method = "X" });
    monitor.RecordState(hub, SignalRState.Disconnected, note: "Closed");
    monitor.Paused = false;
    Check("pause skips messages but keeps state changes", skipped == 0 && hub.Events.Since(0).Last().State == "Closed");

    var json = JsonNode.Parse(monitor.HubsJson())!;
    var h = json["hubs"]![0]!;
    Check("hubs JSON carries state and counters",
        (string?)h["name"] == "state" && (string?)h["state"] == "Disconnected" && (int?)h["reconnects"] == 2 && (int?)h["capacity"] == 1000);
}

// ── Protocol decorator: identical bytes, identical parsing ────────────────────────
ProtocolRoundTrip("json", new JsonHubProtocol());
ProtocolRoundTrip("newtonsoft", new NewtonsoftJsonHubProtocol());
ProtocolRoundTrip("messagepack", new MessagePackHubProtocol());

// ── Live: real ASP.NET Core server, real HubConnection ────────────────────────────
var port = FreePort();
var url = $"http://127.0.0.1:{port}";
var server = await StartServer(port);
var monitor2 = new SignalRMonitor();
{
    var builder = new HubConnectionBuilder().WithUrl(url + "/hub");
    HubInspection.Attach(builder, "chat", new SignalRInspectorOptions(), monitor2);
    builder.WithAutomaticReconnect(new FastRetry());   // registered after Attach on purpose: order must not matter
    var connection = builder.Build();
    var received = new List<string>();
    connection.On<string>("Message", m => { lock (received) received.Add(m); });

    await connection.StartAsync();
    var hub = monitor2.Hub("chat");
    await Until(() => hub.State == SignalRState.Connected);
    Check("live: Connected after StartAsync", hub.State == SignalRState.Connected);
    Check("live: connection id matches", hub.ConnectionId == connection.ConnectionId && hub.ConnectionId != null);
    Check("live: transport detected", hub.Transport == "WebSockets");
    Check("live: protocol shown", hub.Protocol == "json v2" || hub.Protocol?.StartsWith("json") == true);
    Check("live: Connecting → Connected on the timeline",
        hub.Events.Since(0).Where(e => e.Kind == SignalRKinds.State).Select(e => e.State).Take(2).SequenceEqual(["Connecting", "Connected"]));

    var echo = await connection.InvokeAsync<string>("Echo", "hello");
    Check("live: Echo round-trips unchanged", echo == "hello");
    await Until(() => hub.Events.Since(0).Any(e => e is { Kind: SignalRKinds.Completion, Method: "Echo" }));
    var events = hub.Events.Since(0);
    var outEcho = events.FirstOrDefault(e => e is { Direction: SignalRDirection.Out, Kind: SignalRKinds.Invocation, Method: "Echo" });
    var inEcho = events.FirstOrDefault(e => e is { Direction: SignalRDirection.In, Kind: SignalRKinds.Completion, Method: "Echo" });
    Check("live: ↑ Echo invocation with id, size and payload",
        outEcho is { InvocationId: not null, Bytes: > 0 } && outEcho.Payload!.Contains("\"hello\""));
    Check("live: ↓ completion names the method and has a duration",
        inEcho != null && inEcho.InvocationId == outEcho!.InvocationId && inEcho.DurationMs >= 0);

    await connection.InvokeAsync("Shout", "hi");
    await Until(() => { lock (received) return received.Contains("server: hi"); });
    Check("live: ↓ server → client invocation recorded",
        hub.Events.Since(0).Any(e => e is { Direction: SignalRDirection.In, Kind: SignalRKinds.Invocation, Method: "Message" } && !e.Injected));

    var login = await connection.InvokeAsync<JsonElementHolder>("Login", new LoginRequest("ann", "s3cret!"));
    await Until(() => hub.Events.Since(0).Any(e => e is { Kind: SignalRKinds.Completion, Method: "Login" }));
    var loginOut = hub.Events.Since(0).First(e => e is { Direction: SignalRDirection.Out, Method: "Login" });
    var loginIn = hub.Events.Since(0).First(e => e is { Kind: SignalRKinds.Completion, Method: "Login" });
    Check("live: password masked in the stored payload", loginOut.Payload!.Contains("***") && !loginOut.Payload.Contains("s3cret!"));
    Check("live: token in the result masked, app still gets it",
        loginIn.Payload!.Contains("\"accessToken\":\"***\"") && login.AccessToken?.StartsWith("eyJ") == true);

    var items = new List<int>();
    await foreach (var item in connection.StreamAsync<int>("Counter", 3))
        items.Add(item);
    await Until(() => hub.Events.Since(0).Count(e => e is { Kind: SignalRKinds.StreamItem, Method: "Counter" }) == 3);
    Check("live: stream items arrive unchanged", items.SequenceEqual([0, 1, 2]));
    Check("live: stream invocation + 3 items + completion recorded",
        hub.Events.Since(0).Any(e => e is { Kind: SignalRKinds.StreamInvocation, Method: "Counter" })
        && hub.Events.Since(0).Count(e => e is { Kind: SignalRKinds.StreamItem, Method: "Counter" }) == 3);

    var big = new string('x', 10_000);
    Check("live: 10 KB argument round-trips", await connection.InvokeAsync<string>("Echo", big) == big);
    await Until(() => hub.Events.Since(0).Any(e => e is { Kind: SignalRKinds.Completion, Truncated: true }));
    Check("live: big payload stored truncated to 4 KB",
        hub.Events.Since(0).Where(e => e.Truncated).All(e => e.Payload!.Length <= 4097));

    // Injection — the app's handler runs as if the server had called it.
    Check("live: injection available on a connected JSON hub", hub.Injector?.InjectionBlocker == null);
    var injectError = monitor2.Inject("chat", "Message", "[\"from the panel\"]");
    await Until(() => { lock (received) return received.Contains("from the panel"); });
    Check("live: injected message reaches the On handler", injectError == null && received.Contains("from the panel"));
    Check("live: injected message marked on the timeline",
        hub.Events.Since(0).Any(e => e is { Injected: true, Method: "Message" }));
    monitor2.Inject("chat", "Message", "\"single value\"");
    await Until(() => { lock (received) return received.Contains("single value"); });
    Check("live: a single JSON value is one argument", received.Contains("single value"));
    Check("live: invalid JSON rejected", monitor2.Inject("chat", "Message", "[oops") is { } e1 && e1.Contains("JSON"));
    monitor2.Inject("chat", "Message", "[1, 2]");
    await Until(() => hub.Events.Since(0).Any(e => e is { Injected: true, Error: not null }));
    Check("live: wrong argument count shows the binding failure", hub.Events.Since(0).Any(e => e is { Injected: true, Error: not null }));
    Check("live: connection still healthy after injections", await connection.InvokeAsync<string>("Echo", "ok") == "ok");
    for (var i = 0; i < 20; i++)
        monitor2.Inject("chat", "Message", $"[\"burst {i}\"]");
    var burst = connection.InvokeAsync<string>("Echo", "during burst");
    await Until(() => { lock (received) return received.Count(m => m.StartsWith("burst ")) == 20; });
    Check("live: 20 injections interleaved with server traffic all delivered, server reply intact",
        received.Count(m => m.StartsWith("burst ")) == 20 && await burst == "during burst");

    // HTTP API of the panel.
    var apiPort = FreePort();
    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://127.0.0.1:{apiPort}/");
    listener.Start();
    var endpoint = new SignalREndpoint(monitor2);
    _ = Task.Run(async () =>
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); } catch { return; }
            if (!await endpoint.TryHandle(ctx, ctx.Request.HttpMethod, ctx.Request.Url!.AbsolutePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
    });
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}") };
    var hubsJson = JsonNode.Parse(await http.GetStringAsync("/api/signalr"))!;
    Check("api: GET /api/signalr lists the hub with state",
        hubsJson["hubs"]!.AsArray().Any(h => (string?)h!["name"] == "chat" && (string?)h["state"] == "Connected"));
    var echoes = JsonNode.Parse(await http.GetStringAsync("/api/signalr/events?hub=chat&method=echo"))!["events"]!.AsArray();
    Check("api: events filtered by hub and method (case-insensitive)",
        echoes.Count > 0 && echoes.All(e => (string?)e!["method"] == "Echo"));
    var lastSeq = (long)hubsJson["lastSeq"]!;
    var none = JsonNode.Parse(await http.GetStringAsync($"/api/signalr/events?since={lastSeq}"))!["events"]!.AsArray();
    Check("api: since= returns only newer events", none.All(e => (long)e!["seq"]! > lastSeq));
    var seq = (long)echoes[0]!["seq"]!;
    var detail = JsonNode.Parse(await http.GetStringAsync($"/api/signalr/event?seq={seq}"))!;
    Check("api: one event with its full payload", (string?)detail["payload"] != null && echoes[0]!["payload"] == null);
    Check("api: unknown event → 404", (await http.GetAsync("/api/signalr/event?seq=999999")).StatusCode == HttpStatusCode.NotFound);
    var injected = await http.PostAsJsonAsync("/api/signalr/inject", new { hub = "chat", method = "Message", args = "[\"via http\"]" });
    await Until(() => { lock (received) return received.Contains("via http"); });
    Check("api: POST inject", injected.IsSuccessStatusCode && received.Contains("via http"));
    var badInject = await http.PostAsJsonAsync("/api/signalr/inject", new { hub = "nope", method = "Message", args = "[]" });
    Check("api: inject into an unknown hub → 400 with a reason",
        badInject.StatusCode == HttpStatusCode.BadRequest && (await badInject.Content.ReadAsStringAsync()).Contains("no hub"));
    await http.PostAsJsonAsync("/api/signalr/pause", new { on = true });
    var before = hub.Events.Count;
    await connection.InvokeAsync<string>("Echo", "while paused");
    await Task.Delay(100);
    Check("api: paused → no new message events", hub.Events.Count == before && monitor2.Paused);
    await http.PostAsJsonAsync("/api/signalr/pause", new { on = false });
    await http.PostAsJsonAsync("/api/signalr/clear", new { hub = "chat" });
    Check("api: clear empties the hub", hub.Events.Count == 0 && hub.MessagesOut == 0);
    listener.Stop();

    // Kill the server: reconnect cycle.
    await server.StopAsync();
    await server.DisposeAsync();
    await Until(() => hub.State == SignalRState.Reconnecting, 10_000);
    Check("live: server killed → Reconnecting with the reason", hub.State == SignalRState.Reconnecting && hub.Reconnects == 1);
    await Until(() => hub.Events.Since(0).Any(e => e.Kind == SignalRKinds.Error && e.State == "Reconnect attempt failed"), 10_000);
    Check("live: failed attempts recorded with their number",
        hub.Events.Since(0).Any(e => e is { Kind: SignalRKinds.Retry, Attempt: >= 2 }));
    server = await StartServer(port);
    await Until(() => hub.State == SignalRState.Connected, 15_000);
    Check("live: server back → Reconnected", hub.State == SignalRState.Connected
        && hub.Events.Since(0).Any(e => e.State == "Reconnected") && hub.ConnectionId == connection.ConnectionId);
    Check("live: reconnect counter = 1", hub.Reconnects == 1);
    Check("live: works after reconnect", await connection.InvokeAsync<string>("Echo", "again") == "again");
    monitor2.Inject("chat", "Message", "[\"after reconnect\"]");
    await Until(() => { lock (received) return received.Contains("after reconnect"); });
    Check("live: injection works on the new transport", received.Contains("after reconnect"));

    await connection.StopAsync();
    await Until(() => hub.State == SignalRState.Disconnected);
    Check("live: StopAsync → Closed", hub.State == SignalRState.Disconnected && hub.Events.Since(0).Last(e => e.Kind == SignalRKinds.State).State == "Closed");
    Check("live: injection refused when disconnected", monitor2.Inject("chat", "Message", "[]") is { } e2 && e2.Contains("not connected"));
    await connection.DisposeAsync();
}

// ── Live: long polling (inherent keep-alive, different read path) ─────────────────
{
    var builder = new HubConnectionBuilder().WithUrl(url + "/hub", o => o.Transports = HttpTransportType.LongPolling);
    HubInspection.Attach(builder, "polling", new SignalRInspectorOptions { CapturePings = false }, monitor2);
    var connection = builder.Build();
    var received = new List<string>();
    connection.On<string>("Message", m => { lock (received) received.Add(m); });
    await connection.StartAsync();
    var hub = monitor2.Hub("polling");
    await Until(() => hub.State == SignalRState.Connected);
    Check("polling: Connected over LongPolling", hub.State == SignalRState.Connected && hub.Transport == "LongPolling");
    Check("polling: Echo works", await connection.InvokeAsync<string>("Echo", "lp") == "lp");
    monitor2.Inject("polling", "Message", "[\"lp inject\"]");
    await Until(() => { lock (received) return received.Contains("lp inject"); }, 10_000);
    Check("polling: injection delivered while a poll is pending", received.Contains("lp inject"));
    Check("polling: still healthy", await connection.InvokeAsync<string>("Echo", "lp2") == "lp2");
    Check("polling: pings counted but not shown", hub.Events.Since(0).All(e => e.Kind != SignalRKinds.Ping));
    await connection.DisposeAsync();
}

// ── Live: MessagePack ─────────────────────────────────────────────────────────────
{
    var builder = new HubConnectionBuilder().WithUrl(url + "/hub");
    HubInspection.Attach(builder, "binary", new SignalRInspectorOptions(), monitor2);
    builder.AddMessagePackProtocol();   // after Attach: the wrapped protocol must still be MessagePack
    var connection = builder.Build();
    await connection.StartAsync();
    var hub = monitor2.Hub("binary");
    await Until(() => hub.State == SignalRState.Connected);
    Check("msgpack: protocol is MessagePack", hub.Protocol?.StartsWith("messagepack") == true);
    Check("msgpack: Echo works", await connection.InvokeAsync<string>("Echo", "mp") == "mp");
    await connection.InvokeAsync<Dictionary<string, object>>("LoginMap", "ann", "hunter2");
    await Until(() => hub.Events.Since(0).Any(e => e is { Kind: SignalRKinds.Completion, Method: "LoginMap" }));
    var outEcho = hub.Events.Since(0).First(e => e is { Direction: SignalRDirection.Out, Method: "Echo" });
    var loginIn = hub.Events.Since(0).First(e => e is { Kind: SignalRKinds.Completion, Method: "LoginMap" });
    Check("msgpack: payload rebuilt from objects", outEcho.Payload!.Contains("\"mp\"") && outEcho.Bytes > 0);
    Check("msgpack: secrets masked in rebuilt payloads", loginIn.Payload!.Contains("***") && !loginIn.Payload.Contains("tok-123"));
    Check("msgpack: injection explains why it is unavailable", hub.Injector?.InjectionBlocker?.Contains("text protocol") == true);
    await connection.DisposeAsync();
}

await server.StopAsync();
await server.DisposeAsync();

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

// ── helpers ──────────────────────────────────────────────────────────────────────

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

void ProtocolRoundTrip(string label, IHubProtocol inner)
{
    var monitor = new SignalRMonitor();
    var session = new HubSession("rt", new SignalRInspectorOptions(), monitor, inner);
    var wrapped = new InspectingHubProtocol(inner, session);
    var binder = new TestBinder();
    HubMessage[] messages =
    [
        new InvocationMessage("1", "Send", ["hello", 42, new Dictionary<string, object?> { ["password"] = "p@ss", ["name"] = "Ann" }]),
        new InvocationMessage("Notify", ["no id"]),
        new StreamInvocationMessage("2", "Counter", [5]),
        new StreamItemMessage("2", 7),
        CompletionMessage.WithResult("1", "done"),
        CompletionMessage.WithError("2", "boom"),
        new CancelInvocationMessage("2"),
        PingMessage.Instance,
        new CloseMessage("bye", allowReconnect: true),
    ];

    var identical = true;
    var parsedSame = true;
    var all = new ArrayBufferWriter<byte>();
    foreach (var message in messages)
    {
        var plain = new ArrayBufferWriter<byte>();
        inner.WriteMessage(message, plain);
        var through = new ArrayBufferWriter<byte>();
        wrapped.WriteMessage(message, through);
        identical &= plain.WrittenSpan.SequenceEqual(through.WrittenSpan);
        identical &= wrapped.GetMessageBytes(message).Span.SequenceEqual(plain.WrittenSpan);
        all.Write(plain.WrittenSpan);
    }

    // Parse all frames from one buffer through both, frame by frame: same messages, same consumption.
    var a = new ReadOnlySequence<byte>(all.WrittenMemory);
    var b = new ReadOnlySequence<byte>(all.WrittenMemory);
    var count = 0;
    while (true)
    {
        var okA = inner.TryParseMessage(ref a, binder, out var ma);
        var okB = wrapped.TryParseMessage(ref b, binder, out var mb);
        if (okA != okB || a.Length != b.Length || ma?.GetType() != mb?.GetType())
            parsedSame = false;
        if (!okA || !okB)
            break;
        count++;
    }

    Check($"{label}: decorator writes byte-identical frames", identical);
    Check($"{label}: decorator parses the same messages and consumes the same bytes", parsedSame && count == messages.Length && a.Length == 0);
    var events = session.Hub.Events.Since(0);
    var send = events.FirstOrDefault(e => e is { Direction: SignalRDirection.In, Method: "Send" });
    Check($"{label}: parsed invocation recorded with masked payload",
        send is { InvocationId: "1", Bytes: > 0 } && send.Payload!.Contains("***") && !send.Payload.Contains("p@ss") && send.Payload.Contains("Ann"));
    Check($"{label}: completion linked to its invocation",
        events.Any(e => e is { Direction: SignalRDirection.In, Kind: SignalRKinds.Completion, Method: "Send" }));
    Check($"{label}: all kinds recognized", new[]
    {
        SignalRKinds.Invocation, SignalRKinds.StreamInvocation, SignalRKinds.StreamItem, SignalRKinds.Completion,
        SignalRKinds.CancelInvocation, SignalRKinds.Ping, SignalRKinds.Close,
    }.All(kind => events.Any(e => e.Kind == kind)));
}

static async Task Until(Func<bool> condition, int timeoutMs = 5000)
{
    var deadline = Environment.TickCount64 + timeoutMs;
    while (!condition() && Environment.TickCount64 < deadline)
        await Task.Delay(20);
}

static int FreePort()
{
    using var socket = new TcpListener(IPAddress.Loopback, 0);
    socket.Start();
    return ((IPEndPoint)socket.LocalEndpoint).Port;
}

static async Task<WebApplication> StartServer(int port)
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));
    builder.Services.AddSignalR().AddMessagePackProtocol();
    var app = builder.Build();
    app.UseWebSockets();
    app.MapHub<TestHub>("/hub");
    await app.StartAsync();
    return app;
}

public sealed class TestHub : Hub
{
    public string Echo(string text) => text;

    public Task Shout(string text) => Clients.Caller.SendAsync("Message", "server: " + text);

    public object Login(LoginRequest request) =>
        new { user = request.User, accessToken = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl", note = "ok" };

    public Dictionary<string, object> LoginMap(string user, string password) =>
        new() { ["user"] = user, ["token"] = "tok-123" };

    public async IAsyncEnumerable<int> Counter(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < count; i++)
        {
            yield return i;
            await Task.Delay(10, cancellationToken);
        }
    }
}

public sealed record LoginRequest(string User, string Password);

public sealed class JsonElementHolder
{
    public string? User { get; set; }

    public string? AccessToken { get; set; }
}

sealed class FastRetry : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < 200 ? TimeSpan.FromMilliseconds(150) : null;
}

sealed class TestBinder : IInvocationBinder
{
    public IReadOnlyList<Type> GetParameterTypes(string methodName) => methodName switch
    {
        "Send" => [typeof(string), typeof(int), typeof(Dictionary<string, object>)],
        "Counter" => [typeof(int)],
        _ => [typeof(string)],
    };

    public Type GetReturnType(string invocationId) => typeof(string);

    public Type GetStreamItemType(string streamId) => typeof(int);
}
