namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

/// <summary>Turns a snapshot into the app-facing verdict: the OnLeak callback, a log line, the panel's badge.</summary>
internal interface ILeakNotifier
{
    /// <summary>The app-type suspects of the latest snapshot, grouped — what the callback last received.</summary>
    IReadOnlyList<LeakedObject> Latest { get; }

    IReadOnlyList<LeakedObject> Summarize(MemorySnapshot snapshot);
}
