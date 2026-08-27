using Microsoft.Diagnostics.Tracing;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// Bytes and counts per type from either runtime's events. CoreCLR's GCAllocationTick already
/// names the type. Mono's profiler events arrive without a manifest — no event or field names, so
/// they are matched by event id and read by field position (the manifest's template order) — and
/// key allocations by vtable id: the names come from the heap-dump vtable references at session
/// start, and from VTableLoaded + ClassLoaded for whatever appears during the recording.
/// </summary>
internal sealed class AllocationTally
{
    const string MonoProvider = "Microsoft-DotNETRuntimeMonoProfiler";

    /// <summary>Event ids from ClrEtwAll.man's MonoProfiler provider.</summary>
    const int ClassLoaded = 16;
    const int VTableLoaded = 19;
    const int GCAllocation = 39;
    const int VTableClassReference = 63;

    readonly Dictionary<string, (long Bytes, int Count)> _byName = [];
    readonly Dictionary<ulong, (long Bytes, int Count)> _byVTable = [];
    readonly Dictionary<ulong, string> _vtableNames = [];
    readonly Dictionary<ulong, ulong> _vtableClasses = [];
    readonly Dictionary<ulong, string> _classNames = [];

    public int Events { get; private set; }

    public long TotalBytes { get; private set; }

    public void Sampled(string typeName, long bytes)
    {
        var type = string.IsNullOrEmpty(typeName) ? "(unknown)" : typeName;
        _byName[type] = Add(_byName.GetValueOrDefault(type), bytes);
        Events++;
        TotalBytes += bytes;
    }

    public void MonoEvent(TraceEvent e)
    {
        if (!e.ProviderName.Equals(MonoProvider, StringComparison.Ordinal))
            return;

        var payload = new MonoPayload(e);
        switch ((int)e.ID)
        {
            case GCAllocation:
            {
                var vtable = payload.UInt64();
                payload.Pointer(); // ObjectID
                var size = (long)payload.UInt64();
                _byVTable[vtable] = Add(_byVTable.GetValueOrDefault(vtable), size);
                Events++;
                TotalBytes += size;
                break;
            }
            case VTableClassReference:
            {
                var vtable = payload.UInt64();
                payload.UInt64(); // ClassID
                payload.UInt64(); // ModuleID
                _vtableNames[vtable] = payload.Utf16();
                break;
            }
            case VTableLoaded:
            {
                var vtable = payload.UInt64();
                _vtableClasses[vtable] = payload.UInt64();
                break;
            }
            case ClassLoaded:
            {
                var classId = payload.UInt64();
                payload.UInt64(); // ModuleID
                _classNames[classId] = payload.Utf16();
                break;
            }
        }
    }

    public IEnumerable<(string Type, long Bytes, int Count)> ByType()
    {
        var merged = new Dictionary<string, (long Bytes, int Count)>(_byName);
        foreach (var (vtable, tally) in _byVTable)
        {
            var type = NameOf(vtable);
            var current = merged.GetValueOrDefault(type);
            merged[type] = (current.Bytes + tally.Bytes, current.Count + tally.Count);
        }
        return merged.OrderByDescending(kv => kv.Value.Bytes).Select(kv => (kv.Key, kv.Value.Bytes, kv.Value.Count));
    }

    string NameOf(ulong vtable)
    {
        if (_vtableNames.TryGetValue(vtable, out var name) && name.Length > 0)
            return name;
        if (_vtableClasses.TryGetValue(vtable, out var classId) && _classNames.TryGetValue(classId, out var className) && className.Length > 0)
            return className;
        return $"(unnamed type 0x{vtable:x})";
    }

    static (long Bytes, int Count) Add((long Bytes, int Count) tally, long bytes) => (tally.Bytes + bytes, tally.Count + 1);
}
