namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>
/// The collection loop a snapshot runs before judging anything. One GC is never enough in MAUI:
/// a handler is released by a finalizer, its platform peer by the bridge a round later, and the
/// pause between rounds lets the main thread process what those released.
/// </summary>
internal static partial class GcRounds
{
    const int RoundDelayMs = 80;

    public static async Task RunAsync(int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            CollectPlatform();
            await Task.Delay(RoundDelayMs).ConfigureAwait(false);
        }
        GC.Collect();
    }

    /// <summary>Android: the Java side has to collect too, or the bridge keeps the peers' managed wrappers.</summary>
    static partial void CollectPlatform();
}
