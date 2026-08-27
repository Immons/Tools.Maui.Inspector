using System.Diagnostics;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// One dotnet-dsrouter for one app. The tools' `--dsrouter` shorthand hardcodes the device port
/// 9000 and the default IPC path, so two apps on one host fight over it; started by hand, each app
/// gets its own TCP port and its own IPC socket, and dumps of several apps run side by side.
///
/// The modes mirror the shorthand: Android connects out (the runtime dials the router, which
/// `adb reverse`s the device's port — the router's own TCP port must be device port + 1, that is
/// how it derives the mapping); an iOS app listens and the router dials it, over usbmux on a
/// device, over loopback in the simulator.
/// </summary>
internal sealed class DsRouterProcess : IDisposable
{
    static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(3);

    Process? _process;

    /// <summary>The IPC endpoint the diagnostic tool connects to.</summary>
    public string Socket { get; private set; } = "";

    public static async Task<(DsRouterProcess? Router, string? Problem)> Start(string dsRouterPath, DumpTarget target, string instanceKey, Func<string, Task> progress)
    {
        var port = target.DiagnosticPort;
        if (port <= 0)
            return (null, "the app did not report a diagnostic port — is it built with Immons.Tools.Maui.Inspector.Diagnostics?");

        var router = new DsRouterProcess { Socket = SocketPath(port, instanceKey) };
        var arguments = target.Platform switch
        {
            // The app connects out: the router listens on port + 1 and reverses the device's port onto it.
            "android" => $"server-server -ipcs \"{router.Socket}\" -tcps 127.0.0.1:{port + 1} --forward-port Android",
            // The app listens: the router dials it (usbmux on a device, loopback in the simulator).
            "ios" when !target.Virtual => $"server-client -ipcs \"{router.Socket}\" -tcpc 127.0.0.1:{port} --forward-port iOS",
            "ios" => $"server-client -ipcs \"{router.Socket}\" -tcpc 127.0.0.1:{port}",
            _ => "",
        };
        if (arguments.Length == 0)
        {
            router.Dispose();
            return (null, $"no diagnostic route for '{target.Platform}'");
        }

        File.Delete(router.Socket);
        var psi = new ProcessStartInfo(dsRouterPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        router._process = Process.Start(psi);
        if (router._process == null)
        {
            router.Dispose();
            return (null, "could not start dotnet-dsrouter");
        }
        _ = Drain(router._process.StandardOutput);
        _ = Drain(router._process.StandardError);

        // The socket appears once the router is listening; a moment either way is normal.
        var deadline = DateTime.UtcNow + StartupGrace;
        while (DateTime.UtcNow < deadline && !File.Exists(router.Socket) && !router._process.HasExited)
            await Task.Delay(100);
        if (router._process.HasExited)
        {
            router.Dispose();
            return (null, "dotnet-dsrouter exited right after starting — is another router already using this app's port?");
        }
        await progress($"dsrouter on port {port} → {Path.GetFileName(router.Socket)}");
        return (router, null);
    }

    /// <summary>
    /// Under the inspector's own folder, one socket per running app: the port alone is not unique —
    /// the same app on an emulator and a simulator carries the same one — so the panel's own
    /// address goes into the name too.
    /// </summary>
    static string SocketPath(int port, string instanceKey)
    {
        var directory = Path.Combine(ToolInstaller.Directory, "sockets");
        Directory.CreateDirectory(directory);
        var key = string.Concat(instanceKey.Where(char.IsLetterOrDigit));
        return OperatingSystem.IsWindows() ? $"maui-inspector-{port}-{key}" : Path.Combine(directory, $"dsrouter-{port}-{key}.socket");
    }

    static async Task Drain(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is not null)
            {
                // the router is chatty; its lines matter only when it fails, which the tool reports itself
            }
        }
        catch
        {
            // the process is gone
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
            _process?.Dispose();
        }
        catch
        {
            // already gone
        }
        _process = null;
        try
        {
            if (Socket.Length > 0 && !OperatingSystem.IsWindows())
                File.Delete(Socket);
        }
        catch
        {
            // the socket file is the router's to remove; a leftover is harmless
        }
    }
}
