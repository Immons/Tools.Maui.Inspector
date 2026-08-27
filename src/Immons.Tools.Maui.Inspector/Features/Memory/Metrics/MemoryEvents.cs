namespace Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

internal sealed record MemoryEvent(DateTime Time, string Kind, string Detail);

/// <summary>
/// What the OS tells the app about memory: iOS memory warnings, Android trim levels and low-memory
/// calls. Kept as markers for the panel's timeline — a warning right before a crash is the story.
/// </summary>
internal static partial class MemoryEvents
{
    const int Limit = 50;

    static readonly RingLog<MemoryEvent> Log = new(Limit);
    static bool _started;

    public static void Start()
    {
        if (_started)
            return;
        _started = true;
        try
        {
            StartPlatform();
        }
        catch
        {
            // no platform hook here
        }
    }

    public static void Record(string kind, string detail) => Log.Add(_ => new MemoryEvent(DateTime.Now, kind, detail));

    /// <summary>Oldest first.</summary>
    public static IReadOnlyList<MemoryEvent> Recent()
    {
        var newestFirst = Log.NewestFirst();
        newestFirst.Reverse();
        return newestFirst;
    }

    static partial void StartPlatform();
}
