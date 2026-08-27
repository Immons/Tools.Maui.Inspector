using System.Runtime.InteropServices;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

/// <summary>
/// iOS has no process API; the number Xcode's gauge shows is task_vm_info.phys_footprint of the
/// own task, and os_proc_available_memory says how much is left before jetsam. mach_task_self is a
/// variable, not a function, so it is read through dlsym.
/// </summary>
internal static partial class MemoryMetrics
{
    const string LibSystem = "/usr/lib/libSystem.dylib";
    const int TaskVmInfo = 22;
    const int TaskVmInfoRev1Count = 38; // natural_t slots up to and including phys_footprint
    const int ResidentSizeOffset = 16;
    const int PhysFootprintOffset = 144;

    [DllImport(LibSystem)]
    static extern int task_info(uint target, int flavor, IntPtr info, ref int count);

    [DllImport(LibSystem)]
    static extern nuint os_proc_available_memory();

    static readonly Lazy<uint> Self = new(() =>
        (uint)Marshal.ReadInt32(NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "mach_task_self_")));

    static partial void SamplePlatform(ref PlatformMemory platform)
    {
        var count = TaskVmInfoRev1Count;
        var buffer = Marshal.AllocHGlobal(count * sizeof(int));
        try
        {
            if (task_info(Self.Value, TaskVmInfo, buffer, ref count) != 0)
                return;
            var footprint = Marshal.ReadInt64(buffer, PhysFootprintOffset);
            var resident = Marshal.ReadInt64(buffer, ResidentSizeOffset);
            platform = new PlatformMemory(footprint > 0 ? footprint : resident, null, null, null, null, AvailableBytes: Available());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static long? Available()
    {
        try
        {
            var available = (long)os_proc_available_memory();
            return available > 0 ? available : null;
        }
        catch
        {
            return null; // not on this OS version
        }
    }
}
