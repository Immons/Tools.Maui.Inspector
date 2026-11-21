namespace Immons.Tools.Maui.Inspector.Web.Hosting;

/// <summary>What the server does when the app returns to the foreground.</summary>
internal enum ResumeAction
{
    /// <summary>Close the listening socket and bind the same port again.</summary>
    Restart,

    /// <summary>Self-probe first, rebuild only when the probe gets no answer.</summary>
    ProbeThenRestart,
}

/// <summary>The decisions behind the listener recovery, kept MAUI-free for the tests.</summary>
internal static class ServerRecoveryPolicy
{
    /// <summary>Watchdog period while the app is in the foreground.</summary>
    public static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(15);

    /// <summary>Consecutive watchdog misses before the socket is rebuilt — a single miss can be a
    /// busy thread pool, two in a row is a dead listener.</summary>
    public const int WatchdogFailuresBeforeRestart = 2;

    /// <summary>Network events come in bursts (Wi-Fi drops, cellular takes over, Wi-Fi returns).</summary>
    public static readonly TimeSpan ConnectivityDebounce = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// iOS reclaims a suspended app's listening sockets, so the socket is rebuilt unconditionally —
    /// the probe would only add its timeout to the wait. Android keeps sockets of frozen/dozing
    /// apps valid (connections just queue up), so there a probe decides. A network change seen
    /// while the app was in the background always forces the rebuild.
    /// </summary>
    public static ResumeAction OnResume(bool isApple, bool networkChangedWhileInactive) =>
        isApple || networkChangedWhileInactive ? ResumeAction.Restart : ResumeAction.ProbeThenRestart;

    /// <summary>
    /// Rebuild when the network comes back or the set of interfaces changes while connected
    /// (Wi-Fi rejoined, cellular ↔ Wi-Fi). Losing the network does nothing — there is nobody to
    /// talk to until it returns, and the return triggers the rebuild.
    /// </summary>
    public static bool ShouldRestartOnConnectivity(
        bool wasConnected, IReadOnlyCollection<string> previousProfiles,
        bool isConnected, IReadOnlyCollection<string> profiles)
    {
        if (!isConnected)
            return false;
        if (!wasConnected)
            return true;
        return !previousProfiles.OrderBy(p => p, StringComparer.Ordinal)
            .SequenceEqual(profiles.OrderBy(p => p, StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
