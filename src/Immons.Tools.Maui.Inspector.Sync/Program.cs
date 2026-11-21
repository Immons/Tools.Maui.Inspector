using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Sync;
using Immons.Tools.Maui.Inspector.Sync.HeapDumps;

var apps = new List<string>();
var src = Directory.GetCurrentDirectory();
var dumps = Path.Combine(Path.GetTempPath(), "maui-inspector", "heapdumps");
var intervalMs = 1000;
var fromNow = false;
var dryRun = false;
var forwardOnly = false;
var discoverOnly = false;
var toolsOnly = false;
var extraTargets = new List<string>(InspectorDiscovery.SplitList(Environment.GetEnvironmentVariable(InspectorDiscovery.HostsVariable)));
var lanScan = Environment.GetEnvironmentVariable(InspectorDiscovery.NoLanScanVariable) is not ("1" or "true" or "yes");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "forward":
            forwardOnly = true;
            break;
        case "discover":
            discoverOnly = true;
            break;
        case "tools":
            toolsOnly = true;
            break;
        case "--app" when i + 1 < args.Length:
            apps.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(u => u.TrimEnd('/')));
            break;
        case "--host" or "--url" when i + 1 < args.Length:
            extraTargets.AddRange(InspectorDiscovery.SplitList(args[++i]));
            break;
        case "--no-lan-scan":
            lanScan = false;
            break;
        case "--src" when i + 1 < args.Length:
            src = Path.GetFullPath(args[++i]);
            break;
        case "--dumps" when i + 1 < args.Length:
            dumps = Path.GetFullPath(args[++i]);
            break;
        case "--no-tool-install":
            DiagnosticTools.AutoInstall = false;
            break;
        case "--interval" when i + 1 < args.Length:
            intervalMs = int.Parse(args[++i]);
            break;
        case "--from-now":
            fromNow = true;
            break;
        case "--dry-run":
            dryRun = true;
            break;
        case "-h" or "--help":
            Console.WriteLine("""
                Sync tool (maui-inspector-sync) — writes live MauiInspector edits back into your XAML sources.

                Typical use: cd into your app's source folder and just run

                    maui-inspector-sync

                It scans localhost ports 9295-9309 for a running inspector (sets up
                `adb forward` automatically when adb is available), sweeps the local Wi-Fi /
                Ethernet /24 subnet(s) on ports 9295-9299 for physical phones and tablets, and
                watches the current folder. Options for anything non-default:

                Just setting up Android port forwarding (no watching):

                    maui-inspector-sync forward

                Listing every running inspector — simulators, emulators and phones on the
                Wi-Fi — with name, model and URL (nothing is watched or changed):

                    maui-inspector-sync discover

                Checking (and installing) the heap-dump tools ahead of the first dump:

                    maui-inspector-sync tools

                It finds inspectors inside connected Android emulators/devices and maps each
                onto a free host port from the 9295-9309 range — useful when the "natural"
                port is already taken by e.g. an iOS simulator app.

                  --app        Base URL(s) of running web inspectors, comma-separated or repeated
                               (skips scanning). One updater serves every device at once.
                  --host, --url  Extra place(s) to look, next to the scan: a device IP or name
                               (tries ports 9295-9309), host:port or a full URL; repeated or
                               comma-separated. Also from the MAUI_INSPECTOR_HOSTS variable.
                  --no-lan-scan  Skip the subnet sweep (or MAUI_INSPECTOR_NO_LAN_SCAN=1) — localhost,
                               adb and --host only.
                  --src        Root folder that contains the XAML sources (searched recursively).
                  --dumps      Folder for .gcdump files (default: <temp>/maui-inspector/heapdumps).
                  --no-tool-install  Never install dotnet-gcdump / dotnet-dsrouter automatically.
                  --interval   Poll interval in milliseconds (default 1000).
                  --from-now   Ignore edits made before the updater started.
                  --dry-run    Print what would change without writing files.

                Enable recording with the "✎ XAML" button in the web inspector. Pair with your
                IDE's XAML Hot Reload for the full WYSIWYG loop.

                Heap dumps: the panel's Memory view orders them, this tool carries them out with
                dotnet-gcdump (+ dotnet-dsrouter for Android/iOS) and posts every type's counts,
                sizes and root paths back to the panel. Missing or outdated tools are installed on
                first use into ~/.maui-inspector/tools — your global tools stay untouched.
                """);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]} (use --help)");
            return 1;
    }
}

if (!Directory.Exists(src))
{
    Console.Error.WriteLine($"Source folder not found: {src}");
    return 1;
}

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

if (toolsOnly)
{
    var tools = new DiagnosticTools();
    var problem = await tools.Check(new DumpTarget("android", true, 0, null), line => { Console.WriteLine(line); return Task.CompletedTask; });
    Console.WriteLine(problem ?? "ready: " + tools.Inventory());
    return problem == null ? 0 : 1;
}

if (forwardOnly)
{
    var forwards = await AdbForwarder.EnsureForwards(http, Enumerable.Range(9295, 15).ToArray());
    if (forwards.Count == 0)
    {
        Console.WriteLine("no inspectors found on connected Android devices (is adb on PATH and the app running with the inspector enabled?)");
        return 1;
    }
    foreach (var forward in forwards)
        Console.WriteLine($"{forward.Serial}  device port {forward.DevicePort} → http://localhost:{forward.HostPort}  ({forward.Device})");
    Console.WriteLine("open any of the URLs above — the Devices tab of each sees the others.");
    return 0;
}

// No explicit --app: watch every inspector found, and keep adopting new ones as they appear.
// One app can be reachable on several host ports (old + new adb forwards) — the /api/ping
// instance nonce keeps each running app watched exactly once.
if (discoverOnly)
{
    var lister = new InspectorDiscovery(http);
    var subnet = lanScan ? InspectorDiscovery.LocalSubnetHosts() : [];
    var started = DateTime.UtcNow;
    var localUrls = Enumerable.Range(9295, 15).Select(p => $"http://localhost:{p}");
    var namedUrls = extraTargets.SelectMany(t => InspectorDiscovery.ExpandTarget(t, Enumerable.Range(9295, 15).ToArray()));
    var all = await lister.ProbeAll(localUrls.Concat(namedUrls), TimeSpan.FromMilliseconds(800));
    if (subnet.Count > 0)
        all.AddRange(await lister.ScanLan(subnet, InspectorDiscovery.LanPorts, TimeSpan.FromMilliseconds(400)));
    var elapsed = DateTime.UtcNow - started;
    if (all.Count == 0)
    {
        Console.WriteLine($"no inspectors found ({(subnet.Count > 0 ? $"localhost + {subnet.Count} LAN hosts" : "localhost")}, {elapsed.TotalSeconds:0.0} s)");
        return 1;
    }
    foreach (var app in all.GroupBy(a => a.Instance))
    {
        var first = app.First();
        Console.WriteLine($"{first.Url}  {first.Label}");
        foreach (var other in app.Skip(1))
            Console.WriteLine($"    also {other.Url}");
    }
    Console.WriteLine($"({all.Select(a => a.Instance).Distinct().Count()} app(s), {(subnet.Count > 0 ? $"localhost + {subnet.Count} LAN hosts" : "localhost")}, {elapsed.TotalSeconds:0.0} s)");
    return 0;
}

var watchedInstances = new HashSet<string>();
var autoDiscover = apps.Count == 0;
int[] scanPorts = Enumerable.Range(9295, 15).ToArray();
var discovery = new InspectorDiscovery(http);
List<System.Net.IPAddress> lanHosts = lanScan && autoDiscover ? InspectorDiscovery.LocalSubnetHosts() : [];
if (autoDiscover)
{
    Console.WriteLine(lanHosts.Count > 0
        ? $"scanning localhost and the local network ({lanHosts.Count} hosts, ports {InspectorDiscovery.LanPorts[0]}-{InspectorDiscovery.LanPorts[^1]}) for running inspectors…"
        : "scanning localhost for running inspectors…");

    var forwards = await AdbForwarder.EnsureForwards(http, scanPorts);
    foreach (var forward in forwards)
        Console.WriteLine($"adb forward: {forward.Serial} device port {forward.DevicePort} → http://localhost:{forward.HostPort}  ({forward.Device})");

    apps.AddRange(await Discover(discovery, scanPorts, extraTargets, lanHosts, [], watchedInstances));
    if (apps.Count == 0)
        Console.WriteLine("none found yet — will keep scanning (start the app with options.EnableWebServer = true)");
}

Console.WriteLine($"Sync tool: watching {(apps.Count > 0 ? string.Join(", ", apps) : "(waiting)")} → {src}{(dryRun ? "  (dry run)" : "")}");
Console.WriteLine("Enable the \"✎ XAML\" toggle in the web inspector to record edits. Ctrl+C to stop.");
var patcher = new XamlPatcher(src, dryRun);
var heapDumps = new HeapDumpService(http, dumps);
var cursors = new Dictionary<string, long>();
var connectedApps = new HashSet<string>();

foreach (var url in apps)
    cursors[url] = fromNow ? await CurrentSeq(http, url) : 0;

var tick = 0;
var knownSerials = await AdbForwarder.SerialsSafe();
while (true)
{
    // The scan is cheap once apps answer; silent ports cost a short timeout each,
    // so a rescan every ~15 s keeps newly launched devices joining automatically.
    if (autoDiscover && tick % Math.Max(1, 15000 / intervalMs) == 0 && tick > 0)
    {
        // A new Android device needs its forwards before any port scan can see it —
        // the expensive probing runs only when the adb device list actually changes.
        var serials = await AdbForwarder.SerialsSafe();
        if (!serials.SetEquals(knownSerials))
        {
            knownSerials = serials;
            foreach (var forward in await AdbForwarder.EnsureForwards(http, scanPorts))
                Console.WriteLine($"adb forward: {forward.Serial} device port {forward.DevicePort} → http://localhost:{forward.HostPort}  ({forward.Device})");
        }

        // The subnet sweep (~1 300 connects) runs every ~60 s; localhost and --host every ~15 s.
        List<System.Net.IPAddress> sweep = tick % Math.Max(1, 60000 / intervalMs) == 0 ? lanHosts : [];
        foreach (var url in await Discover(discovery, scanPorts, extraTargets, sweep, apps, watchedInstances, quiet: true))
        {
            apps.Add(url);
            cursors[url] = fromNow ? await CurrentSeq(http, url) : 0;
            Console.WriteLine($"attached {url}");
        }
    }

    foreach (var url in apps)
    {
        try
        {
            var json = JsonNode.Parse(await http.GetStringAsync($"{url}/api/changes?since={cursors[url]}&caps=el"));
            if (connectedApps.Add(url))
                Console.WriteLine($"connected to {url}");

            var acks = new JsonArray();
            foreach (var node in json?["changes"] as JsonArray ?? [])
            {
                if (node == null)
                    continue;

                var change = new XamlChange(
                    node["source"]?.GetValue<string>() ?? "",
                    node["line"]?.GetValue<int>() ?? 0,
                    node["column"]?.GetValue<int>() ?? 0,
                    node["element"]?.GetValue<string>() ?? "",
                    node["attribute"]?.GetValue<string>() ?? "",
                    node["value"]?.GetValue<string>() ?? "",
                    node["remove"]?.GetValue<bool>() ?? false,
                    node["op"]?.GetValue<string>() ?? "attr");

                var (ok, message) = patcher.Apply(change);
                acks.Add(new JsonObject
                {
                    ["seq"] = node["seq"]?.GetValue<long>() ?? 0,
                    ["ok"] = ok,
                    ["message"] = message,
                });
            }

            // The panel shows a per-field spinner until this ack lands.
            if (acks.Count > 0)
            {
                try
                {
                    await http.PostAsync($"{url}/api/changes/ack",
                        new StringContent(new JsonObject { ["results"] = acks }.ToJsonString()));
                }
                catch
                {
                    // an app that vanished mid-batch will re-serve the changes anyway
                }
            }

            cursors[url] = json?["seq"]?.GetValue<long>() ?? cursors[url];

            // The Memory view's heap-dump orders ride the same loop.
            await heapDumps.Poll(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (connectedApps.Remove(url))
                Console.WriteLine($"{url} not reachable — waiting…");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error ({url}): {ex.Message}");
        }
    }

    await Task.Delay(intervalMs);
    tick++;
}

static async Task<long> CurrentSeq(HttpClient http, string url)
{
    try
    {
        var initial = JsonNode.Parse(await http.GetStringAsync($"{url}/api/changes?since=0&caps=el"));
        return initial?["seq"]?.GetValue<long>() ?? 0;
    }
    catch
    {
        return 0; // app not up yet — it will replay from the start once reachable
    }
}

/// <summary>
/// localhost first (simulators, adb forwards), then the addresses the user named, then the LAN sweep.
/// One app is often reachable several ways (a simulator answers on localhost and on the Mac's LAN
/// address) — the /api/ping instance nonce keeps each app watched exactly once, under its first URL.
/// </summary>
static async Task<List<string>> Discover(
    InspectorDiscovery discovery, int[] ports, IReadOnlyCollection<string> extraTargets,
    IReadOnlyCollection<System.Net.IPAddress> lanHosts, IReadOnlyCollection<string> known,
    HashSet<string> instances, bool quiet = false)
{
    var found = new List<string>();
    var timeout = TimeSpan.FromMilliseconds(quiet ? 400 : 800);
    var local = ports.Select(p => $"http://localhost:{p}");
    var named = extraTargets.SelectMany(t => InspectorDiscovery.ExpandTarget(t, ports));
    var candidates = local.Concat(named).Where(u => !known.Contains(u)).ToList();

    var hits = await discovery.ProbeAll(candidates, timeout);
    if (lanHosts.Count > 0)
        hits.AddRange((await discovery.ScanLan(lanHosts, InspectorDiscovery.LanPorts, TimeSpan.FromMilliseconds(400)))
            .Where(h => !known.Contains(h.Url)));

    foreach (var hit in hits)
    {
        if (!instances.Add(hit.Instance))
        {
            if (!quiet && !found.Contains(hit.Url))
                Console.WriteLine($"  (also reachable at {hit.Url})");
            continue; // same app already watched through another URL
        }
        found.Add(hit.Url);
        if (!quiet)
            Console.WriteLine($"found inspector at {hit.Url}{(hit.Label.Length == 0 ? "" : $"  ({hit.Label})")}");
    }
    return found;
}
