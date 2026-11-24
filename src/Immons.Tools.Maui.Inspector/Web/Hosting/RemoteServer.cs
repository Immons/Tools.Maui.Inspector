using Microsoft.Extensions.Logging;

namespace Immons.Tools.Maui.Inspector.Web.Hosting;

/// <summary>
/// Embedded HTTP server exposing the inspector to a desktop browser. Requests are routed
/// through the <see cref="IHttpEndpoint"/> chain; the listening socket itself is owned by a
/// <see cref="ListenerSupervisor{TContext}"/>, which rebuilds it when the OS breaks it
/// (iOS suspension, network changes) — see <see cref="ServerLifecycle"/>.
/// </summary>
internal static class RemoteServer
{
    /// <summary>Per-process nonce returned by /api/ping — lets the startup probe (and sibling
    /// panels) tell this instance apart from another process shadowing the port (adb forward…).</summary>
    public static readonly string InstanceId = Guid.NewGuid().ToString("N");

    const int AutoPortRangeStart = 9295;
    const int AutoPortRangeSize = 15;

    static ListenerSupervisor<InspectorHttpContext>? _supervisor;
    static IReadOnlyList<IHttpEndpoint> _endpoints = [];
    static bool _forcedPort;
    static int _starting;

    /// <summary>Probes reuse one client; <c>Connection: close</c> on every request makes each probe
    /// open a fresh connection — a kept-alive one would get answered by a listener whose accept is dead.</summary>
    static readonly HttpClient ProbeClient = new(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    /// <summary>Base URL of the running server, null when not started.</summary>
    public static string? Url => Port is { } port ? $"http://localhost:{port}" : null;

    /// <summary>The port the app itself listens on — differs from the browser's port behind adb forward.</summary>
    public static int? Port => _supervisor?.Port;

    /// <summary>Why the server failed to start, null when fine (or not attempted).</summary>
    public static string? StartError { get; private set; }

    /// <summary>How many times the listening socket was rebuilt since start (reported by /api/ping).</summary>
    public static int Restarts => _supervisor?.Restarts ?? 0;

    /// <summary>Null port = auto-assign from the standard range; a value forces that exact port.</summary>
    public static void EnsureStarted(int? port)
    {
        if (Interlocked.Exchange(ref _starting, 1) == 1)
            return;

        _forcedPort = port != null;
        var (first, range) = port is { } forced ? (forced, 0) : (AutoPortRangeStart, AutoPortRangeSize - 1);
        _endpoints = EndpointFactory.CreateAll();
        var supervisor = new ListenerSupervisor<InspectorHttpContext>(
            Bind,
            ProbeForOurNonce,
            context => FireAndForget.Run(() => HandleSafe(context), "request"),
            (level, message) => Announce(message, level == ServerLogLevel.Warning ? LogLevel.Warning : LogLevel.Information));
        _supervisor = supervisor;

        // Off the main thread: probing can wait on sockets. Url stays null until ready.
        FireAndForget.Run(async () =>
        {
            if (!await StartWithFallback(first, range).ConfigureAwait(false))
                return;
            ServerLifecycle.Start();
            FireAndForget.Run(() => supervisor.RunWatchdogAsync(ServerRecoveryPolicy.WatchdogInterval, () => ServerLifecycle.IsActive,
                ServerRecoveryPolicy.WatchdogFailuresBeforeRestart, CancellationToken.None), "watchdog");
        }, "start");
    }

    /// <summary>The app came back to the foreground.</summary>
    internal static void OnResumed(ResumeAction action)
    {
        if (_supervisor?.Port == null)
            return;
        FireAndForget.Run(() => action == ResumeAction.Restart
            ? RestartOrRelocate("app returned to the foreground")
            : EnsureHealthyOrRelocate("app returned to the foreground"), "resume");
    }

    /// <summary>The network came back or switched interfaces while the app is in the foreground.</summary>
    internal static void OnNetworkChanged(string reason)
    {
        if (_supervisor?.Port == null)
            return;
        FireAndForget.Run(() => RestartOrRelocate(reason), "network change");
    }

    static async Task RestartOrRelocate(string reason)
    {
        if (_supervisor is not { } supervisor)
            return;
        if (!await supervisor.RestartAsync(reason).ConfigureAwait(false))
            await Relocate().ConfigureAwait(false);
    }

    static async Task EnsureHealthyOrRelocate(string reason)
    {
        if (_supervisor is not { } supervisor)
            return;
        if (!await supervisor.EnsureHealthyAsync(reason).ConfigureAwait(false) && !supervisor.IsRunning)
            await Relocate().ConfigureAwait(false);
    }

    /// <summary>
    /// Last resort when the same port cannot be bound again: an auto-assigned server moves to another
    /// free port of the range (the panels' scan finds it there). A forced port stays — the watchdog
    /// keeps retrying it.
    /// </summary>
    static async Task Relocate()
    {
        if (_forcedPort || _supervisor is not { } supervisor || supervisor.IsRunning)
            return;
        Announce("the previous port stays blocked — looking for another one in the range", LogLevel.Warning);
        await StartWithFallback(AutoPortRangeStart, AutoPortRangeSize - 1).ConfigureAwait(false);
    }

    static IInspectorListener<InspectorHttpContext> Bind(int port) => new InspectorHttpListener(port, ServerLog);

    /// <summary>Real server failures go to the console and the Logs view as warnings; client disconnects
    /// only to the debugger output — a closed panel tab is not news, and must not crowd the app's logs.</summary>
    static readonly HttpServerLog ServerLog = new(
        (_, message) => Announce(message, LogLevel.Warning),
        message => System.Diagnostics.Debug.WriteLine("[MauiInspector] " + message));

    /// <summary>
    /// Several instances of the same app (multi-simulator hot reload) share one host, and a
    /// port can be shadowed by another process (adb forward binds 127.0.0.1 while our wildcard
    /// bind still "succeeds"). Each candidate is therefore verified by calling our own
    /// /api/ping through loopback and checking the instance nonce.
    /// </summary>
    static async Task<bool> StartWithFallback(int port, int range)
    {
        var supervisor = _supervisor!;
        for (var candidate = port; candidate <= port + range; candidate++)
        {
            var error = await supervisor.TryStartAsync(candidate).ConfigureAwait(false);
            if (error == null)
            {
                StartError = null;
                Announce($"web inspector listening on http://localhost:{candidate}/ "
                    + (range == 0 ? "(forced port)" : "(auto-assigned)")
                    + AdbHint(candidate),
                    LogLevel.Information);
                return true;
            }

            StartError = error;
        }

        Announce($"failed to start the web inspector on port(s) {port}–{port + range}: {StartError}", LogLevel.Warning);
        return false;
    }

    /// <summary>
    /// On Android the port lives in the device's own loopback, so the host needs a forward. That is
    /// maui-inspector-sync's job — it finds every device and maps it onto a free host port by
    /// itself. The manual line is the fallback, and deliberately not the 1:1 mapping: an iOS
    /// simulator app binds the Mac's port of the same number, both bindings survive, and the
    /// browser then gets the two servers in turn.
    /// </summary>
    static string AdbHint(int port)
    {
        try
        {
            return DeviceInfo.Current.Platform == DevicePlatform.Android
                ? " — Android emulator: run maui-inspector-sync, it forwards this port to the host and prints the URL "
                  + $"(by hand: `adb forward tcp:1{port} tcp:{port}`, then http://localhost:1{port}/ — a shifted host port, "
                  + "because an iOS simulator app may already hold this number on the Mac)"
                : "";
        }
        catch
        {
            return ""; // DeviceInfo unavailable (neutral TFM)
        }
    }

    /// <summary>Startup diagnostics go to the platform console and the panel's Logs view.</summary>
    static void Announce(string message, LogLevel level)
    {
        Console.WriteLine("[MauiInspector] " + message);
        try
        {
            InspectorServices.Current.Logs.Write(level, "MauiInspector", message);
        }
        catch
        {
            // the console line is enough
        }
    }

    /// <summary>
    /// Success = our own listener answered the loopback ping with this instance's nonce.
    /// A response carrying a different nonce means another process really owns the port;
    /// a transport failure is reported as such — the two used to be conflated, sending
    /// users hunting for a port conflict that never existed.
    /// </summary>
    static async Task<ProbeResult> ProbeForOurNonce(int port)
    {
        try
        {
            // Deliberately the managed handler: the platform one (AndroidMessageHandler)
            // enforces the app's cleartext policy — which with targetSdk 28+ blocks plain
            // HTTP even to 127.0.0.1 — and routes through the system proxy/VPN. Neither
            // may veto a request this process makes to itself over loopback.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"http://127.0.0.1:{port}{ApiRoutes.Broadcast.Ping}?{ApiRoutes.Broadcast.ProbeQuery}=1");
            request.Headers.ConnectionClose = true;
            using var response = await ProbeClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return body.Contains(InstanceId, StringComparison.Ordinal)
                ? ProbeResult.Success
                : ProbeResult.Failed($"port {port} is shadowed by another process");
        }
        catch (Exception ex)
        {
            return ProbeResult.Failed($"self-probe on port {port} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs one exchange. Never faults — it is started fire-and-forget by the accept loop: a client
    /// that went away is a Debug line, a handler failure a 500 (when nothing was sent yet).
    /// </summary>
    static async Task HandleSafe(InspectorHttpContext context)
    {
        try
        {
            await Handle(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (HttpServerLog.IsClientGone(ex))
                System.Diagnostics.Debug.WriteLine($"[MauiInspector] {context.Request.HttpMethod} {context.Request.Url.AbsolutePath}: client gone ({ex.GetType().Name})");
            else if (!context.Response.HasStarted)
            {
                try
                {
                    await HttpResponse.WriteText(context, 500, ex.Message).ConfigureAwait(false);
                }
                catch
                {
                    // WriteText reports a dropped client as false; nothing else can come out of it
                }
            }
        }
        finally
        {
            // A handler that returned without answering must not keep the connection waiting.
            context.Response.Close();
        }
    }

    static async Task Handle(InspectorHttpContext context)
    {
        var path = context.Request.Url.AbsolutePath;
        var method = context.Request.HttpMethod;

        foreach (var endpoint in _endpoints)
        {
            if (await endpoint.TryHandle(context, method, path).ConfigureAwait(false))
                return;
        }

        await HttpResponse.WriteText(context, 404, "not found").ConfigureAwait(false);
    }
}
