using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector;
using Immons.Tools.Maui.Inspector.Features.NetworkInspection;

var failures = 0;
const long Big = 200L * 1024 * 1024;
var max = MauiInspector.Options.MaxCapturedBodyBytes;

// ── Capture plan: decided from the headers, never by reading ─────────────────────
Check("no body → none", HttpBodyCapture.PlanFor(null, max) == CapturePlan.None);
Check("no Content-Type → binary, not read", HttpBodyCapture.PlanFor(new ByteArrayContent([1, 2, 3]), max) == CapturePlan.None);
Check("octet-stream → none", HttpBodyCapture.PlanFor(Typed(new ByteArrayContent([1]), "application/octet-stream"), max) == CapturePlan.None);
Check("small JSON of known length → buffer", HttpBodyCapture.PlanFor(new StringContent("{}", Encoding.UTF8, "application/json"), max) == CapturePlan.Buffer);
Check("JSON known to be over the cap → none",
    HttpBodyCapture.PlanFor(Sized(Typed(new StreamContent(new Generator(Big)), "application/json"), Big), max) == CapturePlan.None);
Check("JSON of unknown length → tee", HttpBodyCapture.PlanFor(Typed(new StreamContent(new Generator(10)), "application/json"), max) == CapturePlan.Tee);

// ── 200 MB upload, NSUrlSessionHandler style (ReadAsStreamAsync) ─────────────────
foreach (var style in new[] { HandlerStyle.ReadStream, HandlerStyle.CopyTo })
{
    var source = new Generator(Big);
    var h = new Harness();
    long producedWhenSendStarted = -1;
    JsonNode? midway = null;
    var content = Sized(Typed(new StreamContent(source), "application/octet-stream"), Big);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var response = await h.Send(new HttpRequestMessage(HttpMethod.Put, "https://storage.example/bucket/file.mp4?X-Signature=secret") { Content = content },
        async (request, ct) =>
        {
            producedWhenSendStarted = source.Produced;
            var sink = new CountingSink(onProgress: total =>
            {
                if (midway == null && total > Big / 2)
                    midway = JsonNode.Parse(h.Log.ToJson())!["entries"]![0];
            });
            if (style == HandlerStyle.ReadStream)
            {
                await using var stream = await request.Content!.ReadAsStreamAsync(ct);
                await stream.CopyToAsync(sink, 1 << 20, ct);
            }
            else
            {
                await request.Content!.CopyToAsync(sink, ct);
            }
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
        });

    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var name = style == HandlerStyle.ReadStream ? "ReadAsStreamAsync" : "CopyToAsync";
    Check($"200 MB upload ({name}): no buffering — {allocated / 1024 / 1024} MB allocated", allocated < 32L * 1024 * 1024);
    Check($"200 MB upload ({name}): nothing is read before the handler starts sending", producedWhenSendStarted == 0);
    Check($"200 MB upload ({name}): all bytes reach the transport", source.Produced == Big && response.StatusCode == HttpStatusCode.Created);
    Check($"200 MB upload ({name}): listed as pending while it runs", midway?["pending"]?.GetValue<bool>() == true);
    Check($"…with live sent bytes and the Content-Length", midway?["sent"]?.GetValue<long>() > Big / 3
        && midway?["requestLength"]?.GetValue<long>() == Big);
    Check($"…and the URL without its query", midway?["url"]?.GetValue<string>() == "https://storage.example/bucket/file.mp4?…");
    var done = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check($"200 MB upload ({name}): completed with status, not pending", done["pending"] == null && done["status"]!.GetValue<int>() == 201);
    Check($"…binary body not captured", done["hasBody"]!.GetValue<bool>() == true /* response JSON */ && h.Bodies(done)["request"] == null);
}

// ── Textual bodies of unknown length: copied in transit, never buffered ahead ────
{
    var json = "{\"items\":[" + string.Join(",", Enumerable.Range(0, 1000)) + "]}";
    var source = new Generator(Encoding.UTF8.GetBytes(json));
    var h = new Harness();
    long producedWhenSendStarted = -1;
    await h.Send(new HttpRequestMessage(HttpMethod.Post, "https://api.example/upload") { Content = Typed(new StreamContent(source), "application/json") },
        async (request, ct) =>
        {
            producedWhenSendStarted = source.Produced;
            await request.Content!.CopyToAsync(new CountingSink(), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
    var entry = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("chunked JSON upload: not read before sending", producedWhenSendStarted == 0);
    Check("chunked JSON upload: body captured on the way", h.Bodies(entry)["request"]?.GetValue<string>() == json);
}

{
    var big = Typed(new StreamContent(new Generator(max + 10L)), "application/json");
    var h = new Harness();
    await h.Send(new HttpRequestMessage(HttpMethod.Post, "https://api.example/upload") { Content = big },
        async (request, ct) =>
        {
            await request.Content!.CopyToAsync(new CountingSink(), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
    var entry = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("chunked JSON over the cap: body dropped, not truncated", h.Bodies(entry)["request"] == null);
}

// ── 200 MB download ──────────────────────────────────────────────────────────────
{
    var source = new Generator(Big);
    var h = new Harness();
    var response = await h.Send(new HttpRequestMessage(HttpMethod.Get, "https://cdn.example/video.mp4"),
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Typed(new StreamContent(source), "application/octet-stream"),
        }));
    Check("200 MB download: handler returns before any of the body is read", source.Produced == 0);
    var afterHeaders = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("…the row has its status as soon as the headers are in", afterHeaders["pending"] == null && afterHeaders["status"]!.GetValue<int>() == 200);
    await using (var stream = await response.Content.ReadAsStreamAsync())
        await stream.CopyToAsync(new CountingSink(), 1 << 20);
    var afterBody = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("…received bytes are counted while the app reads", afterBody["received"]?.GetValue<long>() == Big && source.Produced == Big);
}

{
    var json = "{\"page\":1,\"rows\":[1,2,3]}";
    var h = new Harness();
    var response = await h.Send(new HttpRequestMessage(HttpMethod.Get, "https://api.example/rows"),
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Typed(new StreamContent(new Generator(Encoding.UTF8.GetBytes(json))), "application/json"),
        }));
    var before = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("chunked JSON response: not read by the interceptor", h.Bodies(before)["response"] == null);
    var text = await response.Content.ReadAsStringAsync();
    var after = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("chunked JSON response: the app still gets the whole body", text == json);
    Check("…and the Network tab has it once the app read it", h.Bodies(after)["response"]?.GetValue<string>() == json);
    Check("…and the scenario recorder too", h.Recorder.Bodies.Contains(json));
}

{
    var h = new Harness();
    await h.Send(new HttpRequestMessage(HttpMethod.Get, "https://api.example/me"),
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":7}", Encoding.UTF8, "application/json") }));
    var entry = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("small JSON response of known length: captured right away", h.Bodies(entry)["response"]?.GetValue<string>() == "{\"id\":7}");
}

// ── Pending row while the server thinks ──────────────────────────────────────────
{
    var h = new Harness();
    var release = new TaskCompletionSource<HttpResponseMessage>();
    var call = h.Send(new HttpRequestMessage(HttpMethod.Post, "https://api.example/CreateUpload?sig=1")
        { Content = new StringContent("{\"name\":\"a.mp4\"}", Encoding.UTF8, "application/json") }, (_, _) => release.Task);
    await Task.Delay(60);
    var pending = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("a running call is listed before it finishes", pending["pending"]?.GetValue<bool>() == true && pending["method"]!.GetValue<string>() == "POST");
    Check("…with a live duration", pending["ms"]!.GetValue<double>() >= 50);
    Check("…and its request body already", h.Bodies(pending)["request"]?.GetValue<string>() == "{\"name\":\"a.mp4\"}");
    release.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
    await call;
    var done = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("…then completed with the full URL", done["pending"] == null && done["url"]!.GetValue<string>().EndsWith("?sig=1"));
}

{
    var h = new Harness();
    try
    {
        await h.Send(new HttpRequestMessage(HttpMethod.Get, "https://api.example/x"), (_, _) => throw new HttpRequestException("offline"));
    }
    catch (HttpRequestException)
    {
    }
    var entry = JsonNode.Parse(h.Log.ToJson())!["entries"]![0]!;
    Check("a failed call is completed with its error", entry["pending"] == null && entry["error"]!.GetValue<string>() == "HttpRequestException");
}

// ── Breakpoints on bodies of unknown length: bounded peek ────────────────────────
{
    var h = new Harness();
    h.Breakpoints.Requests = true;
    var json = "{\"a\":1}";
    string? received = null;
    await h.Send(new HttpRequestMessage(HttpMethod.Post, "https://api.example/a") { Content = Typed(new StreamContent(new Generator(Encoding.UTF8.GetBytes(json))), "application/json") },
        async (request, _) =>
        {
            received = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
    Check("breakpoint sees a small chunked body", h.Breakpoints.Seen.FirstOrDefault()?.Body == json);
    Check("…and the server still gets it", received == json);
}

{
    var h = new Harness();
    h.Breakpoints.Requests = true;
    var size = max + 1000L;
    var source = new Generator(size);
    GeneratorProbe.Last = source;
    long delivered = 0;
    await h.Send(new HttpRequestMessage(HttpMethod.Post, "https://api.example/a") { Content = Typed(new StreamContent(source), "application/json") },
        async (request, ct) =>
        {
            var sink = new CountingSink();
            await request.Content!.CopyToAsync(sink, ct);
            delivered = sink.Total;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
    Check("breakpoint on a big chunked body: shows no body", h.Breakpoints.Seen.FirstOrDefault()?.Body == "");
    Check("…reads at most the cap ahead", h.Breakpoints.ProducedAtPause <= max + 1);
    Check("…and every byte still reaches the server", delivered == size);
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

static HttpContent Typed(HttpContent content, string mediaType)
{
    content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
    return content;
}

static HttpContent Sized(HttpContent content, long length)
{
    content.Headers.ContentLength = length;
    return content;
}

enum HandlerStyle
{
    ReadStream,
    CopyTo,
}

sealed class Harness
{
    public readonly NetworkLog Log = new();
    public readonly FakeRecorder Recorder = new();
    public readonly FakeBreakpoints Breakpoints = new();
    readonly NetworkInterceptor _interceptor;

    public Harness() => _interceptor = new NetworkInterceptor(Log, new NoRules(), Breakpoints, Recorder);

    public Task<HttpResponseMessage> Send(HttpRequestMessage request, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> inner) =>
        _interceptor.SendAsync(request, inner, CancellationToken.None);

    public JsonNode Bodies(JsonNode entry) => JsonNode.Parse(Log.BodiesJson(entry["seq"]!.GetValue<long>())!)!;
}

/// <summary>Non-seekable source that makes its bytes only when read — "Produced" shows how far
/// anyone has read ahead.</summary>
sealed class Generator : Stream
{
    readonly long _length;
    readonly byte[]? _data;
    long _position;

    public Generator(long length) => _length = length;

    public Generator(byte[] data)
    {
        _data = data;
        _length = data.Length;
    }

    public long Produced => Interlocked.Read(ref _position);

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
        var n = (int)Math.Min(buffer.Length, _length - _position);
        if (n <= 0)
            return 0;
        if (_data != null)
            _data.AsSpan((int)_position, n).CopyTo(buffer);
        else
            buffer[..n].Fill((byte)'a');
        Interlocked.Add(ref _position, n);
        return n;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>The "network": swallows bytes and counts them.</summary>
sealed class CountingSink(Action<long>? onProgress = null) : Stream
{
    public long Total;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => Total;

    public override long Position
    {
        get => Total;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Total += buffer.Length;
        onProgress?.Invoke(Total);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

sealed class NoRules : IMockRules
{
    public IReadOnlyList<MockRule> All => [];
    public bool MockingEnabled => true;
    public string ActiveScenario => "";
    public IReadOnlyList<string> ScenarioNames => [];
    public MockRule Save(MockRule rule) => rule;
    public void Import(IReadOnlyList<string> scenarios, string activeScenario, IReadOnlyList<MockRule> rules) { }
    public bool Remove(int id) => false;
    public bool SetEnabled(int id, bool enabled) => false;
    public MockRule? Match(string method, string url) => null;
    public void SetMockingEnabled(bool enabled) { }
    public void SetActiveScenario(string scenario) { }
    public void AddScenario(string name) { }
    public void RemoveScenario(string name) { }
}

sealed class FakeBreakpoints : IBreakpointGate
{
    public bool Requests;
    public readonly List<InterceptedCall> Seen = [];
    public long ProducedAtPause = -1;
    long _id;

    public bool CaptureRequests => Requests;
    public bool CaptureResponses => false;
    public string UrlFilter => "";
    public IReadOnlyList<InterceptedCall> Pending => [];
    public void Configure(bool captureRequests, bool captureResponses, string urlFilter) { }
    public long NextId() => ++_id;
    public bool ShouldCapture(InterceptPhase phase, string url) => phase == InterceptPhase.Request && Requests;

    public Task<InterceptDecision?> WaitAsync(InterceptedCall call, CancellationToken cancellationToken)
    {
        Seen.Add(call);
        ProducedAtPause = GeneratorProbe.Last?.Produced ?? -1;
        return Task.FromResult<InterceptDecision?>(null); // resume unchanged
    }

    public bool Resume(long id, string? body, int? status) => true;
    public bool Abort(long id) => true;
}

static class GeneratorProbe
{
    public static Generator? Last;
}

sealed class FakeRecorder : IScenarioRecorder
{
    public readonly List<string?> Bodies = [];
    public bool Recording => true;
    public int Count => Bodies.Count;
    public void Start() { }
    public void Capture(string method, string url, int status, string? responseBody) => Bodies.Add(responseBody);
    public int StopAndSave(string scenarioName) => 0;
    public void Cancel() { }
}

namespace Immons.Tools.Maui.Inspector
{
    /// <summary>Stand-in for the MAUI facade: only the body cap is read by the linked code.</summary>
    internal static class MauiInspector
    {
        public static StubOptions Options { get; } = new();
    }

    internal sealed class StubOptions
    {
        public int MaxCapturedBodyBytes { get; set; } = 4 * 1024 * 1024;
    }
}
