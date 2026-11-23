using Android.OS;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal static partial class MemoryMetrics
{
    /// <summary>
    /// Debug.getMemoryInfo walks the whole memory map — tens of milliseconds normally, hundreds with
    /// large mappings or while a collection holds the runtime — so not every second, and never on
    /// the main thread (an ANR dump caught the UI thread inside it, called from a snapshot's
    /// classification and the navigation ledger, both of which run there).
    /// </summary>
    static readonly ThrottledProbe<PssReading> Pss = new(
        TimeSpan.FromSeconds(5),
        ReadPss,
        work => ThreadPool.UnsafeQueueUserWorkItem(static w => w(), work, preferLocal: false));

    sealed record PssReading(long? Pss, long? Graphics);

    static partial void SamplePlatform(ref PlatformMemory platform)
    {
        var runtime = Java.Lang.Runtime.GetRuntime();
        var jni = Java.Interop.JniEnvironment.Runtime;
        var pss = Pss.Refresh(OnMainThread(), System.Environment.TickCount64);
        platform = new PlatformMemory(
            WorkingSet(),
            runtime == null ? null : runtime.TotalMemory() - runtime.FreeMemory(),
            Debug.NativeHeapAllocatedSize,
            jni.GlobalReferenceCount,
            jni.WeakGlobalReferenceCount,
            PssBytes: pss?.Pss,
            GraphicsBytes: pss?.Graphics);
    }

    static PssReading ReadPss()
    {
        // A named section, so a systrace/Perfetto capture shows which thread pays for the read.
        Android.OS.Trace.BeginSection("MauiInspector.getMemoryInfo");
        try
        {
            using var info = new Debug.MemoryInfo();
            Debug.GetMemoryInfo(info);
            return new PssReading(
                info.TotalPss * 1024L,
                OperatingSystem.IsAndroidVersionAtLeast(23) && long.TryParse(info.GetMemoryStat("summary.graphics"), out var graphicsKb)
                    ? graphicsKb * 1024L
                    : null);
        }
        finally
        {
            Android.OS.Trace.EndSection();
        }
    }

    static bool OnMainThread()
    {
        try
        {
            return Microsoft.Maui.ApplicationModel.MainThread.IsMainThread;
        }
        catch
        {
            return true; // unsure — do not risk the UI thread
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
