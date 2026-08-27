namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

/// <summary>What the platform adds to the managed numbers; null = not available here.</summary>
/// <param name="ProcessBytes">Resident footprint of the process.</param>
/// <param name="JavaHeapBytes">Android: the Java heap in use.</param>
/// <param name="NativeHeapBytes">Android: the native (malloc) heap.</param>
/// <param name="GlobalRefs">Android: JNI global references.</param>
/// <param name="WeakGlobalRefs">Android: JNI weak global references.</param>
/// <param name="AvailableBytes">iOS: what the process may still allocate before jetsam.</param>
/// <param name="PssBytes">Android: proportional set size, what the system counts against the app.</param>
/// <param name="GraphicsBytes">Android: graphics memory (textures, bitmaps on the GPU side).</param>
internal readonly record struct PlatformMemory(
    long? ProcessBytes,
    long? JavaHeapBytes,
    long? NativeHeapBytes,
    int? GlobalRefs,
    int? WeakGlobalRefs,
    long? AvailableBytes = null,
    long? PssBytes = null,
    long? GraphicsBytes = null);

/// <summary>One reading of the process' memory.</summary>
internal readonly record struct MemorySample(
    DateTime Time,
    long ManagedBytes,
    long AllocatedBytes,
    int Gen0,
    int Gen1,
    int Gen2,
    PlatformMemory Platform);
