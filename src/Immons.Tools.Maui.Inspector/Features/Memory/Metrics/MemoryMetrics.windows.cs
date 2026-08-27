namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal static partial class MemoryMetrics
{
    static partial void SamplePlatform(ref PlatformMemory platform) =>
        platform = new PlatformMemory(Environment.WorkingSet, null, null, null, null);
}
