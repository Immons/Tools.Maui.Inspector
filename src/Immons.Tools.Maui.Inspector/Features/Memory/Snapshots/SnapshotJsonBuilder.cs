using System.Text.Json.Nodes;
using Immons.Tools.Maui.Inspector.Features.Memory.Metrics;
using Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

/// <summary>JSON of a snapshot; with the previous one, every row carries how much its live count moved.</summary>
internal static class SnapshotJsonBuilder
{
    public static JsonObject Build(MemorySnapshot snapshot, MemorySnapshot? previous, IHeapDumpRequests? dumps = null)
    {
        var before = previous?.Rows.ToDictionary(r => (r.Type, r.Kind), r => r.Alive) ?? [];
        var rows = new JsonArray();
        foreach (var row in snapshot.Rows)
        {
            rows.Add(new JsonObject
            {
                ["type"] = row.Type,
                ["name"] = row.Name,
                ["kind"] = row.Kind.ToString(),
                ["app"] = row.App,
                ["alive"] = row.Alive,
                ["attached"] = row.Attached,
                ["detached"] = row.Detached,
                ["collected"] = row.Collected,
                ["collectedTotal"] = row.CollectedSinceBaseline,
                ["baseDelta"] = row.BaselineDelta,
                ["delta"] = previous == null ? null : row.Alive - before.GetValueOrDefault((row.Type, row.Kind)),
            });
        }

        // Read once, not once per suspect: the report is big and the suspects are many.
        var chains = dumps == null ? null : DumpChains.Latest(dumps);
        var suspects = new JsonArray();
        foreach (var suspect in snapshot.Suspects)
            suspects.Add(Build(suspect, chains?.GetValueOrDefault(suspect.Type)));

        return new JsonObject
        {
            ["time"] = snapshot.Time.ToString("HH:mm:ss"),
            ["elapsedMs"] = (int)snapshot.Elapsed.TotalMilliseconds,
            ["rounds"] = snapshot.Rounds,
            ["totals"] = new JsonObject
            {
                ["tracked"] = snapshot.Totals.Tracked,
                ["alive"] = snapshot.Totals.Alive,
                ["attached"] = snapshot.Totals.Attached,
                ["detached"] = snapshot.Totals.Detached,
                ["collected"] = snapshot.Totals.Collected,
                ["collectedTotal"] = snapshot.Totals.CollectedSinceBaseline,
            },
            ["baseline"] = snapshot.Baseline is { } b
                ? new JsonObject
                {
                    ["time"] = b.Time.ToString("HH:mm:ss"),
                    ["cycles"] = b.Cycles,
                    ["alive"] = b.Alive,
                    ["detached"] = b.Detached,
                    ["grew"] = snapshot.Totals.Alive - b.Alive,
                }
                : null,
            ["memory"] = MemoryJsonBuilder.Sample(snapshot.Memory),
            ["rows"] = rows,
            ["suspects"] = suspects,
        };
    }

    static JsonObject Build(Suspect suspect, TypeChains? chains)
    {
        var hints = new JsonArray();
        foreach (var hint in suspect.Hints)
            hints.Add(hint);
        var parents = new JsonArray();
        foreach (var parent in suspect.Parents)
            parents.Add(parent);
        var holders = new JsonArray();
        foreach (var holder in suspect.Holders)
            holders.Add(holder);
        return new JsonObject
        {
            ["id"] = suspect.Id,
            ["type"] = suspect.Type,
            ["name"] = suspect.Name,
            ["kind"] = suspect.Kind.ToString(),
            ["app"] = suspect.App,
            ["ageMs"] = (long)suspect.Age.TotalMilliseconds,
            ["survived"] = suspect.Survived,
            ["owner"] = suspect.Owner,
            ["hints"] = hints,
            ["parents"] = parents,
            ["holders"] = holders,
            // What only a heap dump can say: the shortest chains to a GC root, and what that root is.
            ["chains"] = chains == null ? null : new JsonArray(chains.Chains.Select(chain => (JsonNode)new JsonArray(chain.Select(step => (JsonNode)step).ToArray())).ToArray()),
            ["rootKind"] = chains?.RootKind,
            ["retained"] = chains?.Retained,
            ["dumpJob"] = chains?.JobId,
        };
    }

    /// <summary>
    /// What a heap dump should explain: the app types among the suspects. A framework type (Label,
    /// Button) would match every instance in the dump, the living ones included — only when no app
    /// type is detached do the most numerous detached types stand in.
    /// </summary>
    public static IReadOnlyList<string> SuspectTypes(MemorySnapshot? snapshot)
    {
        if (snapshot == null)
            return [];
        var app = snapshot.Suspects.Where(s => s.App).Select(s => s.Type).Distinct().ToList();
        return app.Count > 0
            ? app
            : snapshot.Rows.Where(r => r.Detached > 0).OrderByDescending(r => r.Detached).Take(FallbackSuspectTypes).Select(r => r.Type).ToList();
    }

    const int FallbackSuspectTypes = 5;
}
