using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// What /api/ping tells a script about the device it reached — enough to pick the right phone
/// among several and to build its URL: names, app id, port, the device's LAN addresses.
/// </summary>
internal static class DeviceIdentity
{
    public static void Describe(JsonObject json)
    {
        Try(() =>
        {
            var device = DeviceInfo.Current;
            json["deviceName"] = device.Name;
            json["model"] = device.Model;
            json["manufacturer"] = device.Manufacturer;
            json["osVersion"] = device.VersionString;
            json["idiom"] = device.Idiom.ToString().ToLowerInvariant();
        });
        Try(() =>
        {
            json["appId"] = AppInfo.Current.PackageName;
            json["appVersion"] = AppInfo.Current.VersionString;
            json["appBuild"] = AppInfo.Current.BuildString;
        });

        var port = RemoteServer.Port;
        json["port"] = port;
        var addresses = new JsonArray();
        var urls = new JsonArray();
        foreach (var address in LanAddresses())
        {
            addresses.Add(address);
            if (port != null)
                urls.Add($"http://{address}:{port}");
        }
        json["addresses"] = addresses;
        json["urls"] = urls;
        json["remoteControl"] = MauiInspector.Options.EnableRemoteControl;
    }

    /// <summary>Window size and pixel scale — asked on the main thread by the caller.</summary>
    public static JsonObject Window(IWindowInspector inspector) => new()
    {
        ["width"] = inspector.WindowSize.Width,
        ["height"] = inspector.WindowSize.Height,
        ["scale"] = NativeUi.Scale(),
    };

    /// <summary>Non-loopback IPv4 addresses of interfaces that are up (Wi-Fi, Ethernet, USB tethering).</summary>
    static IEnumerable<string> LanAddresses()
    {
        List<string> found = [];
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up
                    || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(unicast.Address)
                        && !unicast.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                        found.Add(unicast.Address.ToString());
                }
            }
        }
        catch
        {
            // not available on this platform — the caller already knows one address that works
        }
        return found.Distinct();
    }

    static void Try(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Essentials unavailable (neutral TFM)
        }
    }
}
