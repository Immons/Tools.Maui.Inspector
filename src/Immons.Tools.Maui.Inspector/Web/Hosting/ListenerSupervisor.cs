namespace Immons.Tools.Maui.Inspector.Web.Hosting;

/// <summary>One bound listening socket as the supervisor sees it — HttpListener in the app,
/// a fake in the tests.</summary>
internal interface IInspectorListener<TContext>
{
    bool IsListening { get; }

    /// <summary>Next accepted request. Throws once the listener is stopped or broken.</summary>
    Task<TContext> AcceptAsync();

    /// <summary>Releases the socket; must not throw and must fault a pending <see cref="AcceptAsync"/>.</summary>
    void Stop();
}

internal enum ServerLogLevel
{
    Information,
    Warning,
}

/// <summary>Outcome of the loopback self-probe: did our own listener answer with our nonce?</summary>
internal readonly record struct ProbeResult(bool Ok, string? Error)
{
    public static readonly ProbeResult Success = new(true, null);

    public static ProbeResult Failed(string error) => new(false, error);
}

internal sealed class ListenerSupervisorOptions
{
    /// <summary>Waits before each rebind attempt of the same port — the old socket may need a
    /// moment to be released after it is closed.</summary>
    public IReadOnlyList<TimeSpan> RebindDelays { get; init; } =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    public TimeSpan AcceptBackoffStart { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan AcceptBackoffMax { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive accept failures (while still "listening") before the socket is rebuilt.</summary>
    public int AcceptFailuresBeforeRestart { get; init; } = 3;
}

/// <summary>
/// Keeps the embedded server's listening socket alive across what mobile OSes do to it.
/// <para>
/// iOS reclaims the resources of a suspended app's listening sockets (TN2277). After the app
/// comes back the kernel still completes TCP handshakes on the port — <c>nc -z</c> says OPEN —
/// but the accept that HttpListener armed internally never completes and never fails, so
/// <c>GetContextAsync</c> just waits forever while <c>IsListening</c> stays true. Nothing in the
/// process notices. The only cure is to close the socket and bind a fresh one, which this class
/// does on request (foreground, network change), when the accept loop keeps failing, and when the
/// loopback self-probe stops getting answers.
/// </para>
/// MAUI-free on purpose — the tests drive it with fake listeners and an instant clock.
/// </summary>
internal sealed class ListenerSupervisor<TContext>
{
    readonly Func<int, IInspectorListener<TContext>> _bind;
    readonly Func<int, Task<ProbeResult>> _probe;
    readonly Action<TContext> _dispatch;
    readonly Action<ServerLogLevel, string> _log;
    readonly Func<TimeSpan, Task> _delay;
    readonly ListenerSupervisorOptions _options;
    readonly SemaphoreSlim _gate = new(1, 1);

    IInspectorListener<TContext>? _current;
    int _generation;
    int _restartEpoch;
    int _probeFailures;

    public ListenerSupervisor(
        Func<int, IInspectorListener<TContext>> bind,
        Func<int, Task<ProbeResult>> probe,
        Action<TContext> dispatch,
        Action<ServerLogLevel, string> log,
        Func<TimeSpan, Task>? delay = null,
        ListenerSupervisorOptions? options = null)
    {
        _bind = bind;
        _probe = probe;
        _dispatch = dispatch;
        _log = log;
        _delay = delay ?? (d => Task.Delay(d));
        _options = options ?? new ListenerSupervisorOptions();
    }

    /// <summary>The last port that was bound and verified; stays set while a restart is in progress.</summary>
    public int? Port { get; private set; }

    /// <summary>Successful rebuilds of the socket since start.</summary>
    public int Restarts { get; private set; }

    /// <summary>Why the last bind/verify failed; null once a listener runs.</summary>
    public string? LastError { get; private set; }

    public bool IsRunning
    {
        get
        {
            var current = _current;
            return current != null && SafeIsListening(current);
        }
    }

    /// <summary>Binds <paramref name="port"/>, starts its accept loop and verifies it answers.
    /// Null on success, the reason otherwise (nothing stays bound then).</summary>
    public async Task<string?> TryStartAsync(int port)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var error = await BindAndVerifyAsync(port).ConfigureAwait(false);
            LastError = error;
            return error;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Closes the current socket and binds the same port again, retrying while the port is still
    /// held. Requests arriving together (foreground + network change) coalesce into one rebuild.
    /// </summary>
    public Task<bool> RestartAsync(string reason) => RestartCoreAsync(reason, Volatile.Read(ref _restartEpoch));

    /// <summary>
    /// Self-probe through loopback; rebuilds the socket after <paramref name="failuresBeforeRestart"/>
    /// consecutive misses, or right away when the listener is not running at all.
    /// </summary>
    public async Task<bool> EnsureHealthyAsync(string reason, int failuresBeforeRestart = 1)
    {
        if (Port is not { } port)
            return false;

        var epoch = Volatile.Read(ref _restartEpoch);
        if (!IsRunning)
            return await RestartCoreAsync($"{reason}: listener not running", epoch).ConfigureAwait(false);

        ProbeResult probe;
        try
        {
            probe = await _probe(port).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            probe = ProbeResult.Failed(ex.Message);
        }

        if (probe.Ok)
        {
            _probeFailures = 0;
            return true;
        }

        var failures = Interlocked.Increment(ref _probeFailures);
        if (failures < Math.Max(1, failuresBeforeRestart))
        {
            _log(ServerLogLevel.Information, $"{reason}: self-probe missed ({failures}/{failuresBeforeRestart}): {probe.Error}");
            return false;
        }

        _probeFailures = 0;
        return await RestartCoreAsync($"{reason}: self-probe failed: {probe.Error}", epoch).ConfigureAwait(false);
    }

    /// <summary>Periodic <see cref="EnsureHealthyAsync"/> while <paramref name="isActive"/> says the app
    /// is in the foreground — a suspended app answers nothing and must not be "fixed".</summary>
    public async Task RunWatchdogAsync(TimeSpan interval, Func<bool> isActive, int failuresBeforeRestart, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await _delay(interval).ConfigureAwait(false);
                if (!cancellation.IsCancellationRequested && isActive())
                    await EnsureHealthyAsync("watchdog", failuresBeforeRestart).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log(ServerLogLevel.Warning, $"watchdog error: {ex.Message}");
            }
        }
    }

    /// <summary>Stops the listener for good (tests, app shutdown).</summary>
    public void Stop()
    {
        StopCurrent();
        Port = null;
    }

    /// <summary><paramref name="seenEpoch"/> is the restart epoch when the request was made: a rebuild
    /// that finished since then (while this request waited for the gate) already answered it.</summary>
    async Task<bool> RestartCoreAsync(string reason, int seenEpoch)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Port is not { } port)
                return false;

            // Someone rebuilt the socket while this request waited — don't do it twice.
            if (Volatile.Read(ref _restartEpoch) != seenEpoch && IsRunning)
                return true;

            try
            {
                return await RebuildAsync(port, reason).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Increment(ref _restartEpoch);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Caller holds the gate.</summary>
    async Task<bool> RebuildAsync(int port, string reason)
    {
        _log(ServerLogLevel.Information, $"restarting the web inspector listener on port {port} ({reason})");
        StopCurrent();

        var attempt = 0;
        foreach (var wait in _options.RebindDelays)
        {
            attempt++;
            if (wait > TimeSpan.Zero)
                await _delay(wait).ConfigureAwait(false);

            var error = await BindAndVerifyAsync(port).ConfigureAwait(false);
            if (error == null)
            {
                Restarts++;
                LastError = null;
                _probeFailures = 0;
                _log(ServerLogLevel.Information,
                    $"web inspector listening again on port {port}" + (attempt > 1 ? $" (attempt {attempt})" : ""));
                return true;
            }

            LastError = error;
        }

        _log(ServerLogLevel.Warning, $"could not rebind port {port} after {attempt} attempts: {LastError}");
        return false;
    }

    /// <summary>Caller holds the gate. Leaves nothing bound when verification fails.</summary>
    async Task<string?> BindAndVerifyAsync(int port)
    {
        IInspectorListener<TContext> listener;
        try
        {
            listener = _bind(port);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        var generation = Interlocked.Increment(ref _generation);
        _current = listener;
        _ = Task.Run(() => AcceptLoop(listener, generation));

        ProbeResult probe;
        try
        {
            probe = await _probe(port).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            probe = ProbeResult.Failed(ex.Message);
        }

        if (probe.Ok)
        {
            Port = port;
            return null;
        }

        StopCurrent();
        return probe.Error ?? $"self-probe on port {port} failed";
    }

    void StopCurrent()
    {
        // Bump first: the loop of the listener being stopped must see it was intentional.
        Interlocked.Increment(ref _generation);
        var current = Interlocked.Exchange(ref _current, null);
        if (current == null)
            return;
        try
        {
            current.Stop();
        }
        catch
        {
            // already dead — nothing to release
        }
    }

    async Task AcceptLoop(IInspectorListener<TContext> listener, int generation)
    {
        var failures = 0;
        while (Volatile.Read(ref _generation) == generation)
        {
            TContext context;
            try
            {
                context = await listener.AcceptAsync().ConfigureAwait(false);
                failures = 0;
            }
            catch (Exception ex)
            {
                if (Volatile.Read(ref _generation) != generation)
                    return; // stopped on purpose (restart / shutdown)

                failures++;
                var listening = SafeIsListening(listener);
                _log(ServerLogLevel.Warning,
                    $"accept failed ({failures}x{(listening ? "" : ", listener closed")}): {ex.GetType().Name}: {ex.Message}");

                if (!listening || failures >= _options.AcceptFailuresBeforeRestart)
                {
                    // Fire and forget: the restart stops this listener, which ends this loop.
                    _ = RestartCoreAsync($"accept loop failed: {ex.Message}", Volatile.Read(ref _restartEpoch));
                    return;
                }

                await _delay(Backoff(failures)).ConfigureAwait(false);
                continue;
            }

            try
            {
                _dispatch(context);
            }
            catch (Exception ex)
            {
                _log(ServerLogLevel.Warning, $"request dispatch failed: {ex.Message}");
            }
        }
    }

    TimeSpan Backoff(int failures)
    {
        var ms = _options.AcceptBackoffStart.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 16));
        return TimeSpan.FromMilliseconds(Math.Min(ms, _options.AcceptBackoffMax.TotalMilliseconds));
    }

    static bool SafeIsListening(IInspectorListener<TContext> listener)
    {
        try
        {
            return listener.IsListening;
        }
        catch
        {
            return false;
        }
    }
}
