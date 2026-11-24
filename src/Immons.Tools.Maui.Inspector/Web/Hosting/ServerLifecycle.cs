namespace Immons.Tools.Maui.Inspector.Web.Hosting;

/// <summary>
/// Feeds the app's foreground/background transitions and network changes to the web server, so it
/// can rebuild a listening socket the OS broke while the app was away (see <see cref="ListenerSupervisor{TContext}"/>).
/// </summary>
internal static partial class ServerLifecycle
{
    static bool _started;
    static volatile bool _active = true;
    static volatile bool _networkChangedWhileInactive;
    static bool _wasConnected;
    static string[] _profiles = [];
    static CancellationTokenSource? _debounce;

    /// <summary>False while the app is in the background — the watchdog sleeps then.</summary>
    public static bool IsActive => _active;

    public static void Start()
    {
        if (_started)
            return;
        _started = true;
        try
        {
            StartPlatform();
        }
        catch (Exception ex)
        {
            Log($"no app lifecycle hook ({ex.Message}) — the watchdog alone keeps the server alive");
        }

        StartConnectivity();
    }

    internal static void OnBackground() => _active = false;

    internal static void OnForeground()
    {
        if (_active && !_networkChangedWhileInactive)
            return; // already in the foreground (e.g. Android: a second activity of the app started)
        _active = true;
        var action = ServerRecoveryPolicy.OnResume(IsApple, _networkChangedWhileInactive);
        _networkChangedWhileInactive = false;
        RemoteServer.OnResumed(action);
    }

    static bool IsApple => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    static void StartConnectivity()
    {
        try
        {
            var connectivity = Connectivity.Current;
            (_wasConnected, _profiles) = Snapshot(connectivity);
            connectivity.ConnectivityChanged += OnConnectivityChanged;
        }
        catch (Exception ex)
        {
            // Android without ACCESS_NETWORK_STATE throws here — foreground + watchdog still cover it.
            Log($"network changes not observed ({ex.Message})");
        }
    }

    static void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool connected;
        string[] profiles;
        try
        {
            (connected, profiles) = Snapshot(Connectivity.Current);
        }
        catch
        {
            return;
        }

        var restart = ServerRecoveryPolicy.ShouldRestartOnConnectivity(_wasConnected, _profiles, connected, profiles);
        _wasConnected = connected;
        _profiles = profiles;
        if (!restart)
            return;

        if (!_active)
        {
            // Rebuild on the way back to the foreground — a suspended app cannot serve anyway.
            _networkChangedWhileInactive = true;
            return;
        }

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _debounce, cts)?.Cancel();
        FireAndForget.Run(async () =>
        {
            try
            {
                await Task.Delay(ServerRecoveryPolicy.ConnectivityDebounce, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // a newer event of the same burst takes over
            }

            RemoteServer.OnNetworkChanged(profiles.Length == 0 ? "network back" : "network: " + string.Join("+", profiles));
        }, "network-change debounce");
    }

    static (bool Connected, string[] Profiles) Snapshot(IConnectivity connectivity) =>
        (connectivity.NetworkAccess is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Local,
         connectivity.ConnectionProfiles.Select(p => p.ToString()).ToArray());

    static void Log(string message)
    {
        try
        {
            InspectorServices.Current.Logs.Write(Microsoft.Extensions.Logging.LogLevel.Debug, "MauiInspector", message);
        }
        catch
        {
            // logging must never break startup
        }
    }

    static partial void StartPlatform();
}
