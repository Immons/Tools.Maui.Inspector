namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

/// <summary>
/// Process memory reading: the GC's own numbers everywhere, plus the platform's view of the
/// process — Java heap, native heap and JNI reference counts on Android (the classic leak signal
/// there), the physical footprint on iOS, the working set on Windows.
/// </summary>
internal static partial class MemoryMetrics
{
    public static MemorySample Sample()
    {
        var platform = default(PlatformMemory);
        try
        {
            SamplePlatform(ref platform);
        }
        catch
        {
            // best effort — the managed numbers are still worth showing
        }

        return new MemorySample(
            DateTime.Now,
            GC.GetTotalMemory(false),
            GC.GetTotalAllocatedBytes(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            platform);
    }

    static partial void SamplePlatform(ref PlatformMemory platform);
}
