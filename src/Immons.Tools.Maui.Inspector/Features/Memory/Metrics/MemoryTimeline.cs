namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal sealed class MemoryTimeline : IMemoryTimeline
{
    const int Limit = 300;

    readonly RingLog<MemorySample> _samples = new(Limit);

    public MemorySample Record()
    {
        var sample = MemoryMetrics.Sample();
        _samples.Add(_ => sample);
        return sample;
    }

    public IReadOnlyList<MemorySample> Recent()
    {
        var newestFirst = _samples.NewestFirst();
        newestFirst.Reverse();
        return newestFirst;
    }

    public void Clear() => _samples.Clear();
}
