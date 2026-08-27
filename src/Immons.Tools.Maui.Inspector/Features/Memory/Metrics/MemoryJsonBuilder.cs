using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal static class MemoryJsonBuilder
{
    public static JsonObject Sample(MemorySample sample) => new()
    {
        ["time"] = sample.Time.ToString("HH:mm:ss"),
        ["managed"] = sample.ManagedBytes,
        ["allocated"] = sample.AllocatedBytes,
        ["gen0"] = sample.Gen0,
        ["gen1"] = sample.Gen1,
        ["gen2"] = sample.Gen2,
        ["process"] = sample.Platform.ProcessBytes,
        ["javaHeap"] = sample.Platform.JavaHeapBytes,
        ["nativeHeap"] = sample.Platform.NativeHeapBytes,
        ["grefs"] = sample.Platform.GlobalRefs,
        ["weakGrefs"] = sample.Platform.WeakGlobalRefs,
        ["available"] = sample.Platform.AvailableBytes,
        ["pss"] = sample.Platform.PssBytes,
        ["graphics"] = sample.Platform.GraphicsBytes,
    };

    public static JsonArray Events(IEnumerable<MemoryEvent> events)
    {
        var array = new JsonArray();
        foreach (var e in events)
            array.Add(new JsonObject { ["time"] = e.Time.ToString("HH:mm:ss"), ["kind"] = e.Kind, ["detail"] = e.Detail });
        return array;
    }

    public static JsonArray Samples(IEnumerable<MemorySample> samples)
    {
        var array = new JsonArray();
        foreach (var sample in samples)
            array.Add(Sample(sample));
        return array;
    }
}
