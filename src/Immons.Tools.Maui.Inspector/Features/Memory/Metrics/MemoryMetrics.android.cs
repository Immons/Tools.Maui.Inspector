using Android.OS;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal static partial class MemoryMetrics
{
    /// <summary>Debug.getMemoryInfo asks the kernel for the whole mapping — tens of milliseconds, so not every second.</summary>
    static readonly TimeSpan PssInterval = TimeSpan.FromSeconds(5);
    static DateTime _pssRead;
    static long? _pss, _graphics;

    static partial void SamplePlatform(ref PlatformMemory platform)
    {
        var runtime = Java.Lang.Runtime.GetRuntime();
        var jni = Java.Interop.JniEnvironment.Runtime;
        RefreshPss();
        platform = new PlatformMemory(
            WorkingSet(),
            runtime == null ? null : runtime.TotalMemory() - runtime.FreeMemory(),
            Debug.NativeHeapAllocatedSize,
            jni.GlobalReferenceCount,
            jni.WeakGlobalReferenceCount,
            PssBytes: _pss,
            GraphicsBytes: _graphics);
    }

    static void RefreshPss()
    {
        if (DateTime.Now - _pssRead < PssInterval)
            return;
        _pssRead = DateTime.Now;
        try
        {
            var info = new Debug.MemoryInfo();
            Debug.GetMemoryInfo(info);
            _pss = info.TotalPss * 1024L;
            _graphics = long.TryParse(info.GetMemoryStat("summary.graphics"), out var graphicsKb) ? graphicsKb * 1024L : null;
        }
        catch
        {
            _pss = _graphics = null;
        }
    }

    static long? WorkingSet()
    {
        try
        {
            var set = System.Environment.WorkingSet;
            return set > 0 ? set : null;
        }
        catch
        {
            return null;
        }
    }
}
