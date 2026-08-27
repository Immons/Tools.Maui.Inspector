using Microsoft.Extensions.Logging;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Watch;

internal sealed class LeakNotifier : ILeakNotifier
{
    string _lastSignature = "";

    public IReadOnlyList<LeakedObject> Latest { get; private set; } = [];

    public LeakNotifier(ISnapshotRunner snapshots, ILogSink logs)
    {
        snapshots.Taken += snapshot => Notify(snapshot, logs);
    }

    public IReadOnlyList<LeakedObject> Summarize(MemorySnapshot snapshot) => snapshot.Suspects
        .Where(s => s.App)
        .GroupBy(s => (s.Type, s.Kind))
        .Select(g => new LeakedObject(g.Key.Type, g.Key.Kind.ToString(), g.Count(), g.SelectMany(s => s.Holders).Distinct().ToList()))
        .OrderByDescending(l => l.Count)
        .ToList();

    void Notify(MemorySnapshot snapshot, ILogSink logs)
    {
        Latest = Summarize(snapshot);
        // Once per new set of suspects — watch mode would otherwise repeat itself after every pop.
        var signature = string.Join(";", snapshot.Suspects.Where(s => s.App).Select(s => s.Id).OrderBy(id => id));
        if (Latest.Count == 0 || signature == _lastSignature)
        {
            _lastSignature = signature;
            return;
        }
        _lastSignature = signature;

        var summary = string.Join(", ", Latest.Take(5).Select(l => $"{TypeNames.ShortName(l.Type)} ×{l.Count}"));
        logs.Write(LogLevel.Warning, "MauiInspector", $"leak suspects after snapshot: {summary}");
        Console.WriteLine($"[MauiInspector] leak suspects: {summary}");
        try
        {
            MauiInspector.Options.Memory.OnLeak?.Invoke(Latest);
        }
        catch (Exception ex)
        {
            logs.Write(LogLevel.Error, "MauiInspector", "OnLeak callback threw: " + ex.Message);
        }
    }
}
