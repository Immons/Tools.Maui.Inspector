using Immons.Tools.Maui.Inspector.Features.Memory.Metrics;
using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>Per-type census line of a snapshot: how many instances survived the collections, and where they stand.</summary>
internal sealed record TypeRow(
    string Type,
    string Name,
    TrackedKind Kind,
    bool App,
    int Alive,
    int Attached,
    int Detached,
    int Collected)
{
    /// <summary>Objects of this type collected since the baseline (or since the app started, without one).</summary>
    public int CollectedSinceBaseline { get; init; }

    /// <summary>How many more (or fewer) are alive than when the baseline was marked; null without one.</summary>
    public int? BaselineDelta { get; init; }
}

/// <summary>An object that is still alive although nothing in a window uses it any more — a leak until proven otherwise.</summary>
internal sealed record Suspect(
    int Id,
    string Type,
    string Name,
    TrackedKind Kind,
    bool App,
    TimeSpan Age,
    int Survived,
    string? Owner,
    IReadOnlyList<string> Hints,
    IReadOnlyList<string> Parents,
    IReadOnlyList<string> Holders);

internal sealed record SnapshotTotals(int Tracked, int Alive, int Attached, int Detached, int Collected, int CollectedSinceBaseline);

/// <summary>
/// A marked snapshot and what happened since: the growth of every type against it, and how many
/// pages were navigated away in between — "+228 objects per repetition" instead of "394 detached".
/// </summary>
internal sealed record BaselineComparison(DateTime Time, int Cycles, IReadOnlyDictionary<string, int> AliveByType, int Alive, int Detached);

/// <summary>The outcome of one "collect everything, then look who is left" round.</summary>
internal sealed record MemorySnapshot(
    DateTime Time,
    TimeSpan Elapsed,
    int Rounds,
    SnapshotTotals Totals,
    IReadOnlyList<TypeRow> Rows,
    IReadOnlyList<Suspect> Suspects,
    MemorySample Memory)
{
    /// <summary>The marked snapshot this one is measured against, when there is one.</summary>
    public BaselineComparison? Baseline { get; init; }
}
