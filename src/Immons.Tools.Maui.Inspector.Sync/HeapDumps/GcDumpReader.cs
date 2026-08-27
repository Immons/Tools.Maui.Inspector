using Graphs;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>A .gcdump file as dotnet-gcdump serialized it (the vendored PerfView reader): the object graph plus who it came from.</summary>
internal static class GcDumpReader
{
    public static GCHeapDump Read(string file) => new(file);

    /// <summary>
    /// The dump is of whatever runtime answered on the diagnostic port. Two diagnostics-enabled
    /// apps in simulators share the Mac's 127.0.0.1:9000 — only the first binds it, and a dump
    /// ordered for the other would quietly describe the wrong process. The process id tells.
    /// </summary>
    public static string? WrongProcess(GCHeapDump dump, DumpTarget target)
    {
        if (target.Pid <= 0 || dump.ProcessID <= 0 || dump.ProcessID == target.Pid)
            return null;
        return $"the dump came from another process (pid {dump.ProcessID}{(string.IsNullOrEmpty(dump.ProcessName) ? "" : ", " + dump.ProcessName)}), not the app (pid {target.Pid}) — "
            + "another app with the diagnostic port is running on the same host and owns port 9000; stop it and dump again";
    }
}
