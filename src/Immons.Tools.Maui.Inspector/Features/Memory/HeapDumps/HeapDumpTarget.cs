using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

/// <summary>What the desktop tool needs to know to aim dotnet-gcdump at this process.</summary>
internal static class HeapDumpTarget
{
    public static void Describe(JsonObject json, bool syncToolConnected)
    {
        json["app"] ??= AppName();
        json["platform"] = Platform();
        json["virtual"] = IsVirtual();
        json["pid"] = Environment.ProcessId;
        json["diagnostics"] = DiagnosticsPort.Configured;
        json["diagnosticsAvailable"] = DiagnosticsPort.Available;
        json["allocTracking"] = DiagnosticsPort.AllocationTracking;
        json["syncTool"] = syncToolConnected;
    }

    /// <summary>Two apps on one simulator take neighbouring panel ports — the panel says which process it is on.</summary>
    static string AppName()
    {
        try
        {
            return AppInfo.Current.Name;
        }
        catch
        {
            return "";
        }
    }

    static string Platform()
    {
        try
        {
            return DeviceInfo.Current.Platform.ToString().ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    static bool IsVirtual()
    {
        try
        {
            return DeviceInfo.Current.DeviceType == DeviceType.Virtual;
        }
        catch
        {
            return false;
        }
    }
}
