using System.Collections.Concurrent;
using System.Threading.Channels;
using Immons.Tools.Maui.Inspector.Web.Hosting;

var failures = 0;
const int Port = 9295;

// ── Start and the accept loop ─────────────────────────────────────────────────────
{
    var h = new Harness();
    Check("start binds and verifies the port", await h.S.TryStartAsync(Port) == null && h.S.Port == Port && h.S.IsRunning);
    h.Latest.Push("a");
    h.Latest.Push("b");
    Check("accepted requests are dispatched", await WaitUntil(() => h.Dispatched.Count == 2));
}

{
    var h = await Harness.Started();
    h.Latest.Fail(new IOException("transient"));
    h.Latest.Push("after");
    Check("one accept failure does not kill the loop", await WaitUntil(() => h.Dispatched.Contains("after")));
    Check("one accept failure does not rebuild the socket", h.Bound.Count == 1 && h.S.Restarts == 0);
}

{
    var h = await Harness.Started();
    var first = h.Latest;
    for (var i = 0; i < 3; i++)
        first.Fail(new IOException("broken"));
    Check("repeated accept failures rebuild the socket", await WaitUntil(() => h.Bound.Count == 2 && h.S.Restarts == 1));
    Check("…on the same port", h.Latest.Port == Port && h.S.Port == Port);
    Check("…and the broken listener is closed", first.Stopped);
    h.Latest.Push("fresh");
    Check("the new listener serves requests", await WaitUntil(() => h.Dispatched.Contains("fresh")));
}

{
    var h = await Harness.Started();
    var first = h.Latest;
    first.Listening = false;
    first.Fail(new ObjectDisposedException("listener"));
    Check("a closed listener is rebuilt right away", await WaitUntil(() => h.Bound.Count == 2 && h.S.Restarts == 1));
    Check("…without waiting for more failures", first.AcceptCalls == 1);
}

{
    var h = await Harness.Started();
    var dispatchThrows = new Harness(dispatch: _ => throw new InvalidOperationException("handler bug"));
    await dispatchThrows.S.TryStartAsync(Port);
    dispatchThrows.Latest.Push("x");
    dispatchThrows.Latest.Push("y");
    Check("a throwing dispatch keeps the loop alive", await WaitUntil(() => dispatchThrows.Latest.AcceptCalls >= 3));
    Check("…and does not rebuild the socket", dispatchThrows.Bound.Count == 1);
    _ = h;
}

// ── Restart (foreground / network change) ─────────────────────────────────────────
{
    var h = await Harness.Started();
    var first = h.Latest;
    await WaitUntil(() => first.AcceptCalls >= 1);
    var hangingAccept = first.PendingAccept();
    Check("restart rebinds the same port", await h.S.RestartAsync("foreground") && h.Bound.Count == 2 && h.Latest.Port == Port);
    Check("…stops the old listener and ends its pending accept",
        first.Stopped && hangingAccept.IsCompleted && hangingAccept.Status != TaskStatus.RanToCompletion);
    await Task.Delay(50);
    Check("…and the old loop does not trigger a second restart", h.Bound.Count == 2 && h.S.Restarts == 1);
}

{
    var h = await Harness.Started();
    h.BusyAttempts[Port] = 2;
    Check("restart retries while the port is still held", await h.S.RestartAsync("foreground"));
    Check("…and binds on the third attempt", h.BindAttempts == 4 && h.Logs.Any(l => l.Contains("attempt 3")));
    Check("…waiting between attempts", h.Delays.Count(d => d > TimeSpan.Zero) >= 2);
}

{
    var h = await Harness.Started();
    h.BusyAttempts[Port] = 1000;
    Check("restart gives up after the configured attempts", !await h.S.RestartAsync("foreground"));
    Check("…reports why and is not running", !h.S.IsRunning && h.S.LastError != null);
    Check("…but remembers the port for the next try", h.S.Port == Port);
    h.BusyAttempts[Port] = 0;
    Check("the next health check rebuilds it once the port is free", await h.S.EnsureHealthyAsync("watchdog") && h.S.IsRunning);
}

{
    var h = await Harness.Started();
    var a = h.S.RestartAsync("foreground");
    var b = h.S.RestartAsync("network back");
    var results = await Task.WhenAll(a, b);
    Check("simultaneous restart requests coalesce into one rebuild", results.All(r => r) && h.Bound.Count == 2 && h.S.Restarts == 1);
}

{
    var h = new Harness();
    Check("restart before any start is a no-op", !await h.S.RestartAsync("foreground") && h.Bound.Count == 0);
}

// ── Self-probe: the zombie listener ───────────────────────────────────────────────
{
    // The iOS case: the socket still accepts TCP, but HttpListener's accept never completes.
    var h = await Harness.Started();
    var zombie = h.Latest;
    zombie.Zombie = true;
    Check("a zombie listener is rebuilt by the health check", await h.S.EnsureHealthyAsync("foreground") && h.Bound.Count == 2);
    Check("…the zombie is closed", zombie.Stopped);
    Check("…and the new listener answers", (await h.ProbeNow()).Ok);
}

{
    var h = await Harness.Started();
    Check("a healthy listener is left alone", await h.S.EnsureHealthyAsync("foreground") && h.Bound.Count == 1);
}

{
    var h = await Harness.Started();
    h.Latest.Zombie = true;
    Check("watchdog: the first miss does not restart", !await h.S.EnsureHealthyAsync("watchdog", 2) && h.Bound.Count == 1);
    Check("watchdog: the second miss in a row does", await h.S.EnsureHealthyAsync("watchdog", 2) && h.Bound.Count == 2);
}

{
    var h = await Harness.Started();
    h.Latest.Zombie = true;
    await h.S.EnsureHealthyAsync("watchdog", 2);
    h.Latest.Zombie = false;
    await h.S.EnsureHealthyAsync("watchdog", 2);
    h.Latest.Zombie = true;
    Check("watchdog: a success resets the miss counter", !await h.S.EnsureHealthyAsync("watchdog", 2) && h.Bound.Count == 1);
}

{
    var h = await Harness.Started();
    h.Latest.Zombie = true;
    using var cts = new CancellationTokenSource();
    var active = false;
    var watchdog = h.S.RunWatchdogAsync(TimeSpan.FromMilliseconds(5), () => active, 1, cts.Token);
    await Task.Delay(60);
    Check("watchdog sleeps while the app is in the background", h.Bound.Count == 1 && h.Probes == 1);
    active = true;
    Check("watchdog rebuilds a zombie once the app is active", await WaitUntil(() => h.Bound.Count == 2));
    cts.Cancel();
    await watchdog;
}

// ── Startup verification ──────────────────────────────────────────────────────────
{
    var h = new Harness();
    h.Shadowed.Add(Port);
    var error = await h.S.TryStartAsync(Port);
    Check("a shadowed port fails verification", error != null && error.Contains("shadowed"));
    Check("…and leaves nothing bound", h.Latest.Stopped && !h.S.IsRunning && h.S.Port == null);
    Check("the next port of the range works", await h.S.TryStartAsync(Port + 1) == null && h.S.Port == Port + 1);
}

{
    var h = new Harness();
    h.BusyAttempts[Port] = 1;
    Check("a failing bind reports its error", await h.S.TryStartAsync(Port) is { } e && e.Contains("in use"));
}

// ── Policy ────────────────────────────────────────────────────────────────────────
Check("iOS restarts on resume", ServerRecoveryPolicy.OnResume(isApple: true, networkChangedWhileInactive: false) == ResumeAction.Restart);
Check("Android probes on resume", ServerRecoveryPolicy.OnResume(isApple: false, networkChangedWhileInactive: false) == ResumeAction.ProbeThenRestart);
Check("Android restarts on resume after a network change", ServerRecoveryPolicy.OnResume(false, true) == ResumeAction.Restart);
Check("network back → restart", ServerRecoveryPolicy.ShouldRestartOnConnectivity(false, [], true, ["WiFi"]));
Check("network lost → nothing", !ServerRecoveryPolicy.ShouldRestartOnConnectivity(true, ["WiFi"], false, []));
Check("Wi-Fi → cellular → restart", ServerRecoveryPolicy.ShouldRestartOnConnectivity(true, ["WiFi"], true, ["Cellular"]));
Check("Wi-Fi rejoined next to cellular → restart", ServerRecoveryPolicy.ShouldRestartOnConnectivity(true, ["Cellular"], true, ["Cellular", "WiFi"]));
Check("same profiles in another order → nothing", !ServerRecoveryPolicy.ShouldRestartOnConnectivity(true, ["WiFi", "Cellular"], true, ["Cellular", "WiFi"]));
Check("watchdog needs two misses", ServerRecoveryPolicy.WatchdogFailuresBeforeRestart == 2);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 3000)
{
    var deadline = Environment.TickCount64 + timeoutMs;
    while (Environment.TickCount64 < deadline)
    {
        if (condition())
            return true;
        await Task.Delay(5);
    }
    return condition();
}

/// <summary>Fake "network": binds hand out <see cref="FakeListener"/>s, the probe answers when the
/// newest listener of the port is running and not a zombie, every delay is (almost) instant.</summary>
sealed class Harness
{
    public readonly List<FakeListener> Bound = [];
    public readonly Dictionary<int, int> BusyAttempts = [];
    public readonly HashSet<int> Shadowed = [];
    public readonly ConcurrentQueue<string> Dispatched = new();
    public readonly ConcurrentQueue<string> Logs = new();
    public readonly ConcurrentQueue<TimeSpan> Delays = new();
    public int BindAttempts;
    public int Probes;
    public readonly ListenerSupervisor<string> S;

    public Harness(Action<string>? dispatch = null)
    {
        S = new ListenerSupervisor<string>(
            Bind,
            async port =>
            {
                // A real probe is an HTTP round trip — never completes synchronously.
                await Task.Delay(1).ConfigureAwait(false);
                return Probe(port);
            },
            dispatch ?? (c => Dispatched.Enqueue(c)),
            (level, message) => Logs.Enqueue($"{level}: {message}"),
            d =>
            {
                Delays.Enqueue(d);
                return Task.Delay(1);
            });
    }

    public static async Task<Harness> Started()
    {
        var h = new Harness();
        await h.S.TryStartAsync(9295);
        return h;
    }

    public FakeListener Latest
    {
        get
        {
            lock (Bound)
                return Bound[^1];
        }
    }

    public Task<ProbeResult> ProbeNow() => Task.FromResult(Probe(9295));

    IInspectorListener<string> Bind(int port)
    {
        Interlocked.Increment(ref BindAttempts);
        if (BusyAttempts.TryGetValue(port, out var busy) && busy > 0)
        {
            BusyAttempts[port] = busy - 1;
            throw new InvalidOperationException($"address already in use: {port}");
        }

        var listener = new FakeListener(port);
        lock (Bound)
            Bound.Add(listener);
        return listener;
    }

    ProbeResult Probe(int port)
    {
        Interlocked.Increment(ref Probes);
        if (Shadowed.Contains(port))
            return ProbeResult.Failed($"port {port} is shadowed by another process");
        FakeListener? current;
        lock (Bound)
            current = Bound.LastOrDefault(l => l.Port == port);
        return current is { Stopped: false, Zombie: false, Listening: true }
            ? ProbeResult.Success
            : ProbeResult.Failed("timeout");
    }
}

/// <summary>Scripted accept results; <see cref="Stop"/> faults the pending accept like HttpListener does.</summary>
sealed class FakeListener(int port) : IInspectorListener<string>
{
    readonly Channel<Func<string>> _steps = Channel.CreateUnbounded<Func<string>>();
    readonly CancellationTokenSource _stop = new();
    Task<string>? _pending;
    int _acceptCalls;

    public int Port { get; } = port;
    public volatile bool Listening = true;
    public volatile bool Stopped;

    /// <summary>Kernel still accepts TCP, the process never sees the request — the iOS failure.</summary>
    public volatile bool Zombie;

    public int AcceptCalls => _acceptCalls;

    public bool IsListening => Listening && !Stopped;

    public void Push(string context) => _steps.Writer.TryWrite(() => context);

    public void Fail(Exception ex) => _steps.Writer.TryWrite(() => throw ex);

    /// <summary>The accept the loop is currently blocked in.</summary>
    public Task<string> PendingAccept() => _pending ?? Task.FromResult("");

    public Task<string> AcceptAsync()
    {
        Interlocked.Increment(ref _acceptCalls);
        _pending = Next();
        return _pending;
    }

    async Task<string> Next()
    {
        var step = await _steps.Reader.ReadAsync(_stop.Token).ConfigureAwait(false);
        return step();
    }

    public void Stop()
    {
        Stopped = true;
        _stop.Cancel();
    }
}
