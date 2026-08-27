namespace HeapDumpReaderTests;

/// <summary>A static root, so the sentinels have a path the report must find.</summary>
static class Roots
{
    public static readonly List<LeakSentinel> Held = [];
}

sealed class LeakSentinel
{
    public byte[] Payload { get; } = new byte[4096];
}
