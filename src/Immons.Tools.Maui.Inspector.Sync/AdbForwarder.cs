using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Sync;

/// <summary>
/// Host-side adb plumbing. An Android emulator has its own loopback, so an inspector
/// listening on the device's port 9295 is invisible from the host — and a 1:1
/// `adb forward` can collide with an iOS simulator app, which shares the host network
/// and may already occupy that host port. This finds inspector instances on every
/// connected Android device and maps each onto a free host port from the standard
/// scan range, so the browser and the Devices tab see them all side by side.
/// </summary>
internal static class AdbForwarder
{
    public sealed record Forward(string Serial, int HostPort, int DevicePort, string Device);

    public static async Task<List<Forward>> EnsureForwards(HttpClient http, int[] candidatePorts)
    {
        var result = new List<Forward>();
        List<string> serials;
        try
        {
            serials = await Devices();
        }
        catch
        {
            return result; // adb not installed — iOS-only setup
        }
        if (serials.Count == 0)
            return result;

        var existing = await ExistingForwards();

        foreach (var serial in serials)
        {
            foreach (var devicePort in candidatePorts)
            {
                // A live mapping from a previous run is reused, a dead one replaced.
                var known = existing.FirstOrDefault(f => f.Serial == serial && f.DevicePort == devicePort);
                if (known != null)
                {
                    if (await ProbeInspector(http, known.HostPort) is { } aliveDevice)
                    {
                        result.Add(known with { Device = aliveDevice });
                        continue;
                    }
                    await Adb($"-s {serial} forward --remove tcp:{known.HostPort}");
                }

                // Peek through a temporary forward to see whether an inspector listens there.
                var probe = await Adb($"-s {serial} forward tcp:0 tcp:{devicePort}");
                if (probe.Code != 0 || !int.TryParse(probe.Output.Trim(), out var tempPort))
                    continue;
                var device = await ProbeInspector(http, tempPort);
                await Adb($"-s {serial} forward --remove tcp:{tempPort}");
                if (device == null)
                    continue;

                if (PickFreeHostPort(candidatePorts, devicePort) is not { } hostPort)
                {
                    Console.WriteLine($"{serial}: inspector on device port {devicePort}, but no free host port in {candidatePorts[0]}-{candidatePorts[^1]}");
                    continue;
                }
                if ((await Adb($"-s {serial} forward tcp:{hostPort} tcp:{devicePort}")).Code == 0)
                    result.Add(new Forward(serial, hostPort, devicePort, device));
            }
        }
        return result;
    }

    /// <summary>Serial set for change detection; empty when adb is unavailable.</summary>
    public static async Task<HashSet<string>> SerialsSafe()
    {
        try
        {
            return [.. await Devices()];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Serials of ready devices/emulators; throws when adb is missing.</summary>
    static async Task<List<string>> Devices()
    {
        var (code, output) = await Adb("devices");
        if (code != 0)
            return [];
        return output.Split('\n')
            .Skip(1)
            .Select(line => line.Trim().Split('\t'))
            .Where(parts => parts.Length == 2 && parts[1] == "device")
            .Select(parts => parts[0])
            .ToList();
    }

    /// <summary>
    /// dotnet-dsrouter's Android port forwarding drops every adb forward on the way in, ours to
    /// the inspectors included — the panel would lose the app right when the dump lands. Remember
    /// them before the dump and put back whatever is missing afterwards.
    /// </summary>
    public static async Task<List<Forward>> Snapshot()
    {
        try
        {
            return await ExistingForwards();
        }
        catch
        {
            return []; // no adb — nothing to restore either
        }
    }

    public static async Task<int> Restore(List<Forward> snapshot)
    {
        if (snapshot.Count == 0)
            return 0;
        var restored = 0;
        try
        {
            var current = await ExistingForwards();
            foreach (var forward in snapshot.Where(f => !current.Any(c => c.Serial == f.Serial && c.HostPort == f.HostPort)))
            {
                var (code, _) = await Adb($"-s {forward.Serial} forward tcp:{forward.HostPort} tcp:{forward.DevicePort}");
                if (code == 0)
                    restored++;
            }
        }
        catch
        {
            // adb went away mid-dump; the next scan re-establishes what it can
        }
        return restored;
    }

    static async Task<List<Forward>> ExistingForwards()
    {
        var result = new List<Forward>();
        var (code, output) = await Adb("forward --list");
        if (code != 0)
            return result;
        foreach (var line in output.Split('\n'))
        {
            // "emulator-5554 tcp:9500 tcp:9308"
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3
                && parts[1].StartsWith("tcp:", StringComparison.Ordinal)
                && parts[2].StartsWith("tcp:", StringComparison.Ordinal)
                && int.TryParse(parts[1][4..], out var host)
                && int.TryParse(parts[2][4..], out var device))
                result.Add(new Forward(parts[0], host, device, ""));
        }
        return result;
    }

    /// <summary>Device description when an inspector answers on this host port; null otherwise.</summary>
    static async Task<string?> ProbeInspector(HttpClient http, int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            var json = JsonNode.Parse(await http.GetStringAsync($"http://localhost:{port}/api/tree", cts.Token));
            return json?["device"]?.GetValue<string>() ?? "";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The device's own port when free on the host, else the first free one in range.</summary>
    static int? PickFreeHostPort(int[] candidatePorts, int preferred)
    {
        foreach (var port in candidatePorts.OrderBy(p => p == preferred ? 0 : 1))
            if (IsHostPortFree(port))
                return port;
        return null;
    }

    /// <summary>
    /// Binding is not the test. An iOS simulator app binds the wildcard address (`*:9295`), and on
    /// macOS a later bind of `127.0.0.1:9295` still succeeds — so a bind probe calls a taken port
    /// free, the forward is created anyway, and the browser then gets the two servers in turn.
    /// Connecting is the honest question: if anything accepts on loopback, the port is spoken for.
    /// The bind still runs afterwards, for ports held by something that is not accepting.
    /// </summary>
    static bool IsHostPortFree(int port)
    {
        try
        {
            using var probe = new TcpClient();
            if (probe.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromMilliseconds(250)))
                return false;
        }
        catch
        {
            // Refused (nobody there) or timed out — fall through to the bind test.
        }

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    static async Task<(int Code, string Output)> Adb(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("adb", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("adb not found");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output.Length > 0 ? output : error);
    }
}
