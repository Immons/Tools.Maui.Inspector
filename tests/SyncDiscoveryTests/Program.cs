using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Sync;

var failures = 0;
int[] ports = [9295, 9296, 9297];

// ── Targets from --host / --url / MAUI_INSPECTOR_HOSTS ─────────────────────────────
Check("list splits on , ; and spaces", InspectorDiscovery.SplitList("a, b;c  d").SequenceEqual(["a", "b", "c", "d"]));
Check("empty list", !InspectorDiscovery.SplitList(null).Any() && !InspectorDiscovery.SplitList("  ").Any());
Check("full URL is kept", InspectorDiscovery.ExpandTarget("http://192.168.68.106:9295/", ports).SequenceEqual(["http://192.168.68.106:9295"]));
Check("host:port gets http://", InspectorDiscovery.ExpandTarget("192.168.68.106:9300", ports).SequenceEqual(["http://192.168.68.106:9300"]));
Check("bare IP → every default port", InspectorDiscovery.ExpandTarget("192.168.68.106", ports)
    .SequenceEqual(["http://192.168.68.106:9295", "http://192.168.68.106:9296", "http://192.168.68.106:9297"]));
Check("bare host name → every default port", InspectorDiscovery.ExpandTarget("pixel.local", ports).Count() == 3);
Check("explicit :80 is honoured", InspectorDiscovery.ExpandTarget("10.0.0.2:80", ports).SequenceEqual(["http://10.0.0.2:80"]));
Check("URL without port → every default port", InspectorDiscovery.ExpandTarget("http://10.0.0.2", ports).Count() == 3);
Check("IPv6 literal with port", InspectorDiscovery.ExpandTarget("[fe80::1]:9295", ports).SequenceEqual(["http://[fe80::1]:9295"]));
Check("IPv6 literal without port", InspectorDiscovery.ExpandTarget("[fe80::1]", ports).First() == "http://[fe80::1]:9295");
Check("garbage is ignored", !InspectorDiscovery.ExpandTarget("http://", ports).Any() && !InspectorDiscovery.ExpandTarget("", ports).Any());

// ── Subnets ───────────────────────────────────────────────────────────────────────
var mac = IPAddress.Parse("192.168.68.105");
var hosts24 = InspectorDiscovery.SubnetHosts(mac, 24).ToList();
Check("/24 = 253 hosts (no network, broadcast or self)", hosts24.Count == 253
    && !hosts24.Contains(mac) && hosts24[0].ToString() == "192.168.68.1" && hosts24[^1].ToString() == "192.168.68.254");
Check("/22 is narrowed to the /24 around the address", InspectorDiscovery.SubnetHosts(mac, 22).Count() == 253
    && InspectorDiscovery.SubnetHosts(mac, 22).All(h => h.ToString().StartsWith("192.168.68.", StringComparison.Ordinal)));
Check("/16 is narrowed too", InspectorDiscovery.SubnetHosts(IPAddress.Parse("10.1.2.3"), 16).Count() == 253);
Check("/28 stays /28", InspectorDiscovery.SubnetHosts(IPAddress.Parse("10.0.0.5"), 28).Count() == 13);
Check("/31 and /32 have no one to find", !InspectorDiscovery.SubnetHosts(mac, 31).Any() && !InspectorDiscovery.SubnetHosts(mac, 32).Any());
Check("private ranges", InspectorDiscovery.IsPrivate(IPAddress.Parse("10.9.9.9"))
    && InspectorDiscovery.IsPrivate(IPAddress.Parse("172.16.0.1")) && InspectorDiscovery.IsPrivate(IPAddress.Parse("172.31.255.1"))
    && InspectorDiscovery.IsPrivate(IPAddress.Parse("192.168.1.1")));
Check("public / CGNAT / link-local are not swept", !InspectorDiscovery.IsPrivate(IPAddress.Parse("8.8.8.8"))
    && !InspectorDiscovery.IsPrivate(IPAddress.Parse("100.76.49.107")) && !InspectorDiscovery.IsPrivate(IPAddress.Parse("172.32.0.1"))
    && !InspectorDiscovery.IsPrivate(IPAddress.Parse("169.254.1.1")));
Check("Wi-Fi/Ethernet are swept", InspectorDiscovery.IsScannableInterface("en0", NetworkInterfaceType.Wireless80211)
    && InspectorDiscovery.IsScannableInterface("en7", NetworkInterfaceType.Ethernet));
Check("VPN, AirDrop, bridges are not", !InspectorDiscovery.IsScannableInterface("utun4", NetworkInterfaceType.Unknown)
    && !InspectorDiscovery.IsScannableInterface("awdl0", NetworkInterfaceType.Ethernet)
    && !InspectorDiscovery.IsScannableInterface("bridge100", NetworkInterfaceType.Ethernet)
    && !InspectorDiscovery.IsScannableInterface("en9", NetworkInterfaceType.Tunnel)
    && !InspectorDiscovery.IsScannableInterface("lo0", NetworkInterfaceType.Loopback));
Check("cellular is not (carrier NAT 10.x is no LAN)", !InspectorDiscovery.IsScannableInterface("pdp_ip0", NetworkInterfaceType.Unknown)
    && !InspectorDiscovery.IsScannableInterface("rmnet_data0", NetworkInterfaceType.Unknown));

// ── Labels ────────────────────────────────────────────────────────────────────────
Check("label from 0.9.22+ wins", InspectorDiscovery.Describe(JsonNode.Parse(
    """{"app":"KidsTime","device":"iPhone · iOS 27.2","label":"KidsTime · iPhone (iPhone18,2) · iOS 27.2 · Device"}"""))
    == "KidsTime · iPhone (iPhone18,2) · iOS 27.2 · Device");
Check("older physical iPhone gets name + model", InspectorDiscovery.Describe(JsonNode.Parse(
    """{"app":"KidsTime","device":"iPhone · iOS 27.2","deviceName":"iPhone","model":"iPhone18,2","virtual":false}"""))
    == "KidsTime · iPhone (iPhone18,2) · iOS 27.2 · Device");
Check("older simulator stays as it was", InspectorDiscovery.Describe(JsonNode.Parse(
    """{"app":"Sample","device":"iPhone 18 Pro · iOS 27.2 · Simulator","model":"arm64","virtual":true}"""))
    == "Sample · iPhone 18 Pro · iOS 27.2 · Simulator");
Check("very old simulator without the virtual flag", InspectorDiscovery.Describe(JsonNode.Parse(
    """{"app":"Sample","device":"iPhone 16 · iOS 18.0 · Simulator"}""")) == "Sample · iPhone 16 · iOS 18.0 · Simulator");
Check("Android phone whose name is its model", InspectorDiscovery.Describe(JsonNode.Parse(
    """{"app":"KidsTime","device":"Pixel 9 · Android 16","deviceName":"Pixel 9","model":"Pixel 9","virtual":false}"""))
    == "KidsTime · Pixel 9 · Android 16 · Device");

// ── LAN sweep (fake network) ──────────────────────────────────────────────────────
{
    // One phone at .106:9295, one TV-like box at .50:9296 that is open but not an inspector,
    // everything else silent: closed ports cost the full connect timeout, like an unanswered SYN.
    var open = new HashSet<string> { "192.168.68.106:9295", "192.168.68.50:9296" };
    var connects = 0;
    var handler = new FakeHttp(url => url switch
    {
        "http://192.168.68.106:9295/api/ping" => """{"app":"KidsTime","instance":"abc","label":"KidsTime · iPhone (iPhone18,2) · iOS 27.2 · Device"}""",
        "http://192.168.68.50:9296/api/ping" => "<html>router</html>",
        _ => null,
    });
    var discovery = new InspectorDiscovery(new HttpClient(handler), async (endpoint, timeout) =>
    {
        Interlocked.Increment(ref connects);
        if (open.Contains(endpoint.ToString()))
            return true;
        await Task.Delay(timeout);
        return false;
    });

    var watch = Stopwatch.StartNew();
    var found = await discovery.ScanLan(hosts24, InspectorDiscovery.LanPorts, TimeSpan.FromMilliseconds(400));
    watch.Stop();
    Check("sweep tries every host × port", connects == 253 * InspectorDiscovery.LanPorts.Length);
    Check("sweep finds the phone", found.Count == 1 && found[0].Url == "http://192.168.68.106:9295" && found[0].Instance == "abc");
    Check("…with its name and model", found[0].Label.Contains("iPhone18,2"));
    Check("…pings only the open ports", handler.Requests == 2);
    Check($"…in under 3 s ({watch.ElapsedMilliseconds} ms for {connects} connects)", watch.ElapsedMilliseconds < 3000);
}

{
    var handler = new FakeHttp(url => url == "http://10.0.0.2:9296/api/ping" ? """{"instance":"x","app":"A","device":"d"}""" : null);
    var discovery = new InspectorDiscovery(new HttpClient(handler));
    var found = await discovery.ProbeAll(InspectorDiscovery.ExpandTarget("10.0.0.2", ports), TimeSpan.FromSeconds(1));
    Check("a bare --host finds the app on whichever port it uses", found.Count == 1 && found[0].Url == "http://10.0.0.2:9296");
    var none = await discovery.ProbeAll(["http://10.0.0.9:9295"], TimeSpan.FromSeconds(1));
    Check("an unreachable --host yields nothing (no exception)", none.Count == 0);
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

/// <summary>Answers from a URL → body map; null = connection refused.</summary>
sealed class FakeHttp(Func<string, string?> respond) : HttpMessageHandler
{
    int _requests;

    public int Requests => _requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        var body = respond(request.RequestUri!.ToString());
        if (body == null)
            throw new HttpRequestException("connection refused");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
