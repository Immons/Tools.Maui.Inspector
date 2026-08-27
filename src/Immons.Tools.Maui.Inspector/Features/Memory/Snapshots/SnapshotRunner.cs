using System.Diagnostics;
using Immons.Tools.Maui.Inspector.Features.Memory.Holders;
using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>
/// Collections run off the main thread (so it can pump the releases they trigger); the
/// classification runs on it, because it reads Parent/Handler/BindingContext of live elements.
/// </summary>
internal sealed class SnapshotRunner(ITrackedInstances instances, IMainThreadDispatcher mainThread, IHolderScanner holders, INavigationLedger ledger) : ISnapshotRunner
{
    const int MinRounds = 1;
    const int MaxRounds = 20;

    const int HistoryLimit = 40;

    readonly SemaphoreSlim _single = new(1, 1);
    readonly List<SnapshotDigest> _history = [];

    public long Sequence { get; private set; }

    public MemorySnapshot? Latest { get; private set; }

    public MemorySnapshot? Previous { get; private set; }

    public IReadOnlyList<SnapshotDigest> History
    {
        get
        {
            lock (_history)
            {
                return _history.ToList();
            }
        }
    }

    public event Action<MemorySnapshot>? Taken;

    public MemorySnapshot? Baseline { get; private set; }

    int _baselineCycles;

    /// <summary>
    /// The baseline is a snapshot plus the navigation count at that moment: what grew, and per how
    /// many repetitions of the flow — the number that separates "it leaks" from "it is just big".
    /// </summary>
    public async Task<MemorySnapshot?> MarkBaselineAsync(bool clear)
    {
        if (clear)
        {
            Baseline = null;
            return null;
        }
        var snapshot = await RunAsync().ConfigureAwait(false);
        instances.ResetCollectedCounters();
        _baselineCycles = ledger.Counts.Cycles;
        Baseline = snapshot;
        return snapshot;
    }

    public async Task<MemorySnapshot> RunAsync()
    {
        await _single.WaitAsync().ConfigureAwait(false);
        try
        {
            var started = Stopwatch.GetTimestamp();
            var rounds = Math.Clamp(MauiInspector.Options.Memory.CollectionsPerSnapshot, MinRounds, MaxRounds);
            await Task.Run(() => GcRounds.RunAsync(rounds)).ConfigureAwait(false);

            var (live, collected) = instances.Prune();
            var comparison = Baseline == null
                ? null
                : new BaselineComparison(Baseline.Time, Math.Max(ledger.Counts.Cycles - _baselineCycles, 0),
                    Baseline.Rows.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Sum(r => r.Alive)),
                    Baseline.Totals.Alive, Baseline.Totals.Detached);
            var cumulative = instances.CollectedSinceReset;
            var snapshot = await mainThread.RunAsync(() =>
                InstanceClassifier.Classify(live, collected, cumulative, comparison, DateTime.Now, Stopwatch.GetElapsedTime(started), rounds, holders)).ConfigureAwait(false);

            Previous = Latest;
            Latest = snapshot;
            Sequence++;
            Remember(snapshot);
            Taken?.Invoke(snapshot);
            return snapshot;
        }
        finally
        {
            _single.Release();
        }
    }

    void Remember(MemorySnapshot snapshot)
    {
        var byType = snapshot.Rows.Where(r => r.App && r.Detached > 0)
            .GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.Sum(r => r.Detached));
        lock (_history)
        {
            _history.Add(new SnapshotDigest(snapshot.Time, snapshot.Totals.Alive, snapshot.Totals.Detached, byType));
            if (_history.Count > HistoryLimit)
                _history.RemoveAt(0);
        }
    }
}
