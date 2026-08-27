namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>Runs leak snapshots, remembers the last two (what grew) and a digest of every recent one (the trend).</summary>
internal interface ISnapshotRunner
{
    MemorySnapshot? Latest { get; }

    MemorySnapshot? Previous { get; }

    /// <summary>Per-snapshot digests, oldest first, bounded.</summary>
    IReadOnlyList<SnapshotDigest> History { get; }

    /// <summary>Raised after every snapshot, manual or watch-mode — the ledger and the notifier listen.</summary>
    event Action<MemorySnapshot>? Taken;

    /// <summary>Counts every snapshot taken — the panel watches it to notice the ones watch mode takes by itself.</summary>
    long Sequence { get; }

    /// <summary>The marked snapshot every later one is compared against, if any.</summary>
    MemorySnapshot? Baseline { get; }

    /// <summary>Marks the state as the baseline (taking a snapshot first) — or clears it.</summary>
    Task<MemorySnapshot?> MarkBaselineAsync(bool clear);

    Task<MemorySnapshot> RunAsync();
}

/// <summary>What the history chart needs from a snapshot: the detached counts of the app types.</summary>
internal sealed record SnapshotDigest(DateTime Time, int Alive, int Detached, IReadOnlyDictionary<string, int> DetachedByType);
