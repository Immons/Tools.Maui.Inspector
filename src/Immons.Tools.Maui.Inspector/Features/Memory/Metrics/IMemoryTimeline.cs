namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

/// <summary>The recent readings behind the panel's sparkline. Sampled when the panel asks — nothing runs while it is closed.</summary>
internal interface IMemoryTimeline
{
    MemorySample Record();

    IReadOnlyList<MemorySample> Recent();

    void Clear();
}
