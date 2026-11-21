using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Sync;

/// <summary>One running inspector, identified by its per-process nonce.</summary>
internal sealed record DiscoveredInspector(string Url, string Instance, string Label);

/// <summary>
/// Where to look for inspectors: localhost (simulators, adb forwards), addresses the user named
/// (<c>--host</c>, <c>MAUI_INSPECTOR_HOSTS</c>) and — for phones on the Wi-Fi — a quick sweep of the
/// Mac's own /24 subnets. The pure parts are static so the tests can reach them without a network.
/// </summary>
internal sealed class InspectorDiscovery
{
    /// <summary>Environment variable with extra addresses: hosts, host:port or URLs, separated by , ; or spaces.</summary>
    public const string HostsVariable = "MAUI_INSPECTOR_HOSTS";

    /// <summary>Set to 1/true to skip the subnet sweep (same as <c>--no-lan-scan</c>).</summary>
    public const string NoLanScanVariable = "MAUI_INSPECTOR_NO_LAN_SCAN";

    /// <summary>Ports swept on LAN hosts — a phone runs one app with the inspector, two at most.</summary>
    public static readonly int[] LanPorts = [9295, 9296, 9297, 9298, 9299];

    readonly HttpClient _http;
    readonly Func<IPEndPoint, TimeSpan, Task<bool>> _connect;

    public InspectorDiscovery(HttpClient http, Func<IPEndPoint, TimeSpan, Task<bool>>? connect = null)
    {
        _http = http;
        _connect = connect ?? TryConnect;
    }

    // ── Probing ───────────────────────────────────────────────────────────────────

    /// <summary>GET /api/ping on every URL at once; the ones that answer like an inspector.</summary>
    public async Task<List<DiscoveredInspector>> ProbeAll(IEnumerable<string> urls, TimeSpan timeout)
    {
        var list = urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new DiscoveredInspector?[list.Count];
        await Task.WhenAll(list.Select(async (url, i) => results[i] = await Probe(url, timeout).ConfigureAwait(false)))
            .ConfigureAwait(false);
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    public async Task<DiscoveredInspector?> Probe(string url, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var json = JsonNode.Parse(await _http.GetStringAsync($"{url}/api/ping", cts.Token).ConfigureAwait(false));
            var instance = json?["instance"]?.GetValue<string>();
            return string.IsNullOrEmpty(instance) ? null : new DiscoveredInspector(url, instance, Describe(json));
        }
        catch
        {
            return null; // nothing (or something else) on this port
        }
    }

    /// <summary>
    /// TCP-connects every host × port of the local /24 subnets in parallel (short timeout, so the
    /// sweep stays under ~3 s), then pings only the ports that accepted.
    /// </summary>
    public async Task<List<DiscoveredInspector>> ScanLan(
        IReadOnlyCollection<IPAddress> hosts, IReadOnlyCollection<int> ports, TimeSpan connectTimeout, int parallelism = 256)
    {
        var open = new ConcurrentBag<string>();
        var endpoints = hosts.SelectMany(h => ports.Select(p => new IPEndPoint(h, p))).ToList();
        using var gate = new SemaphoreSlim(parallelism);
        await Task.WhenAll(endpoints.Select(async endpoint =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (await _connect(endpoint, connectTimeout).ConfigureAwait(false))
                    open.Add(UrlOf(endpoint));
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return await ProbeAll(open.OrderBy(u => u, StringComparer.Ordinal), TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
    }

    static async Task<bool> TryConnect(IPEndPoint endpoint, TimeSpan timeout)
    {
        using var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await socket.ConnectAsync(endpoint, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Targets ───────────────────────────────────────────────────────────────────

    /// <summary>Splits "a, b;c d" — the format of <c>--host</c> values and <see cref="HostsVariable"/>.</summary>
    public static IEnumerable<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// "http://192.168.1.5:9295" → that URL; "192.168.1.5:9300" → http://192.168.1.5:9300;
    /// a bare host ("192.168.1.5", "pixel.local", "[fe80::1]") → that host on every default port.
    /// </summary>
    public static IEnumerable<string> ExpandTarget(string value, IReadOnlyCollection<int> defaultPorts)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.EndsWith("://", StringComparison.Ordinal))
            return [];
        text = text.TrimEnd('/');

        var hasScheme = text.Contains("://", StringComparison.Ordinal);
        if (!Uri.TryCreate(hasScheme ? text : "http://" + text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return [];

        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.DnsSafeHost}]" : uri.Host;
        var scheme = uri.Scheme;
        return HasExplicitPort(text)
            ? [$"{scheme}://{host}:{uri.Port}"]
            : defaultPorts.Select(p => $"{scheme}://{host}:{p}");
    }

    /// <summary>Uri reports 80 for "host" and for "host:80" alike — look at the text instead.</summary>
    static bool HasExplicitPort(string text)
    {
        var authority = text.Contains("://", StringComparison.Ordinal)
            ? text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..]
            : text;
        var slash = authority.IndexOf('/');
        if (slash >= 0)
            authority = authority[..slash];
        var closing = authority.LastIndexOf(']'); // IPv6 literal: the port follows the bracket
        return authority.IndexOf(':', closing + 1) >= 0;
    }

    static string UrlOf(IPEndPoint endpoint) =>
        endpoint.AddressFamily == AddressFamily.InterNetworkV6
            ? $"http://[{endpoint.Address}]:{endpoint.Port}"
            : $"http://{endpoint.Address}:{endpoint.Port}";

    // ── Subnets ───────────────────────────────────────────────────────────────────

    /// <summary>Hosts to sweep: the /24 around every private IPv4 address of a physical interface
    /// that is up (Wi-Fi, Ethernet) — VPN tunnels, AirDrop and VM bridges are left out.</summary>
    public static List<IPAddress> LocalSubnetHosts()
    {
        var hosts = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || !IsScannableInterface(nic.Name, nic.NetworkInterfaceType))
                    continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(unicast.Address))
                        hosts.AddRange(SubnetHosts(unicast.Address, PrefixOf(unicast)));
                }
            }
        }
        catch
        {
            // interface enumeration unavailable — localhost and --host still work
        }
        return hosts.DistinctBy(h => h.ToString()).ToList();
    }

    /// <summary>Not every platform reports the prefix (mobile runtimes may throw) — a /24 is the usual Wi-Fi.</summary>
    static int PrefixOf(UnicastIPAddressInformation unicast)
    {
        try
        {
            return unicast.PrefixLength > 0 ? unicast.PrefixLength : 24;
        }
        catch
        {
            return 24;
        }
    }

    public static bool IsScannableInterface(string name, NetworkInterfaceType type)
    {
        if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
            return false;
        // Cellular (iOS pdp_ip, Android rmnet/ccmni) often carries a carrier-NAT 10.x address — never a LAN.
        string[] virtualPrefixes = ["utun", "awdl", "llw", "anpi", "ipsec", "gif", "stf", "bridge", "vmnet", "vboxnet",
            "docker", "veth", "br-", "tailscale", "zt", "wg", "tun", "tap", "vEthernet", "pdp_ip", "rmnet", "ccmni"];
        return !virtualPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>RFC 1918 only — sweeping a public or CGNAT (Tailscale 100.64/10) range would be rude and slow.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>
    /// Every host of the address's subnet except the network, broadcast and the address itself
    /// (the Mac's own inspectors are found through localhost). Subnets wider than /24 are narrowed
    /// to the /24 around the address — 65 000 hosts are not "a quick sweep".
    /// </summary>
    public static IEnumerable<IPAddress> SubnetHosts(IPAddress address, int prefixLength)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || prefixLength is < 0 or > 32)
            yield break;
        var prefix = Math.Max(prefixLength, 24);
        if (prefix >= 31)
            yield break; // point-to-point: no one else to find

        var bytes = address.GetAddressBytes();
        var value = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        var mask = uint.MaxValue << (32 - prefix);
        var network = value & mask;
        var broadcast = network | ~mask;
        for (var host = network + 1; host < broadcast; host++)
        {
            if (host == value)
                continue;
            yield return new IPAddress([(byte)(host >> 24), (byte)(host >> 16), (byte)(host >> 8), (byte)host]);
        }
    }

    // ── Labels ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Inspector Sample · iPhone 18 Pro · iOS 27.2 · Simulator" — and for a physical device the
    /// model and its LAN address, so two phones of one kind can be told apart.
    /// </summary>
    public static string Describe(JsonNode? ping)
    {
        if (ping == null)
            return "";
        var label = Text(ping["label"]);
        if (label.Length > 0)
            return label; // 0.9.22+: the app builds it itself

        // Older inspectors: assemble it from the pieces they report.
        var app = Text(ping["app"]);
        var device = Text(ping["device"]);
        var model = Text(ping["model"]);
        var isVirtual = Flag(ping["virtual"])
                        || device.EndsWith("Simulator", StringComparison.Ordinal)
                        || device.EndsWith("Emulator", StringComparison.Ordinal);
        if (!isVirtual && model.Length > 0 && !device.Contains(model, StringComparison.OrdinalIgnoreCase))
        {
            var name = Text(ping["deviceName"]);
            device = device.Length == 0
                ? model
                : name.Length > 0 && device.StartsWith(name, StringComparison.Ordinal)
                    ? $"{name} ({model}){device[name.Length..]}"
                    : $"{device} · {model}";
        }

        if (!isVirtual && device.Length > 0 && !device.Contains("Device", StringComparison.Ordinal))
            device += " · Device";
        return string.Join(" · ", new[] { app, device }.Where(s => s.Length > 0));
    }

    static bool Flag(JsonNode? node)
    {
        try
        {
            return node?.GetValue<bool>() ?? false;
        }
        catch
        {
            return false;
        }
    }

    static string Text(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
