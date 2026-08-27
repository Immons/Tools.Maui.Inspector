using Microsoft.Diagnostics.Tracing;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// Reads a Mono profiler event's payload straight from its bytes. TraceEvent has no manifest for
/// this provider — it reports the events by id with no field names and no values — so the fields
/// are taken in the order the runtime's manifest declares them: UInt64s and pointers little-endian,
/// strings null-terminated UTF-16.
/// </summary>
internal ref struct MonoPayload(TraceEvent e)
{
    readonly byte[] _data = e.EventData();
    readonly int _pointerSize = e.PointerSize == 0 ? 8 : e.PointerSize;
    int _offset;

    public ulong UInt64()
    {
        if (_offset + 8 > _data.Length)
            return 0;
        var value = BitConverter.ToUInt64(_data, _offset);
        _offset += 8;
        return value;
    }

    public ulong Pointer()
    {
        if (_offset + _pointerSize > _data.Length)
            return 0;
        var value = _pointerSize == 8 ? BitConverter.ToUInt64(_data, _offset) : BitConverter.ToUInt32(_data, _offset);
        _offset += _pointerSize;
        return value;
    }

    public string Utf16()
    {
        for (var end = _offset; end + 1 < _data.Length; end += 2)
        {
            if (_data[end] != 0 || _data[end + 1] != 0)
                continue;
            var text = System.Text.Encoding.Unicode.GetString(_data, _offset, end - _offset);
            _offset = end + 2;
            return text;
        }
        return "";
    }
}
