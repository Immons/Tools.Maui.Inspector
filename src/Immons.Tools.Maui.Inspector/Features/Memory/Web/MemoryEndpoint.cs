using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Web;

/// <summary>
/// GET /api/memory — a fresh reading plus the recent ones (the sparkline), tracking and hand-off
/// state; POST /api/memory/gc — forces a collection round; GET /api/memory/peers — the platform's
/// peer census (Android).
/// </summary>
internal sealed class MemoryEndpoint(
    IMemoryTimeline timeline,
    IInstanceTracker tracker,
    ITrackedInstances instances,
    IHeapDumpRequests dumps,
    ISnapshotRunner snapshots,
    ISyncTracker sync,
    INavigationLedger ledger,
    ILeakNotifier leaks,
    IServiceLifetimes lifetimes) : IHttpEndpoint
{
    const int ForcedRounds = 2;

    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Memory.Stats)
        {
            await HttpResponse.WriteJson(context, Stats().ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Memory.Gc)
        {
            await GcRounds.RunAsync(ForcedRounds).ConfigureAwait(false);
            var json = new JsonObject { ["ok"] = true, ["sample"] = MemoryJsonBuilder.Sample(timeline.Record()) };
            await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && path == ApiRoutes.Memory.Peers)
        {
            await HttpResponse.WriteJson(context, Peers().ToJsonString()).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    JsonObject Stats()
    {
        var sample = timeline.Record();
        var (open, pending, alive, cycles) = ledger.Counts;
        var singletons = new JsonArray();
        foreach (var singleton in lifetimes.Singletons)
            singletons.Add(singleton);
        var json = new JsonObject
        {
            ["sample"] = MemoryJsonBuilder.Sample(sample),
            ["samples"] = MemoryJsonBuilder.Samples(timeline.Recent()),
            ["events"] = MemoryJsonBuilder.Events(MemoryEvents.Recent()),
            ["tracking"] = new JsonObject { ["enabled"] = tracker.Enabled, ["tracked"] = instances.Count },
            ["settings"] = MemorySettingsJson.Build(),
            ["watch"] = new JsonObject { ["open"] = open, ["pending"] = pending, ["alive"] = alive, ["cycles"] = cycles },
            ["leaks"] = leaks.Latest.Count,
            // Watch mode snapshots by itself; this is how the panel notices and reloads the suspects.
            ["snapshot"] = snapshots.Latest is { } latest
                ? new JsonObject { ["seq"] = snapshots.Sequence, ["time"] = latest.Time.ToString("HH:mm:ss"), ["detached"] = latest.Totals.Detached }
                : null,
            ["singletons"] = singletons,
            ["dump"] = dumps.Active is { } active ? HeapDumpJsonBuilder.Job(active, includeReport: false) : null,
        };
        HeapDumpTarget.Describe(json, sync.Connected);
        return json;
    }

    static JsonObject Peers()
    {
        var census = PlatformPeers.Census();
        var types = new JsonArray();
        foreach (var type in census.Types)
        {
            types.Add(new JsonObject
            {
                ["type"] = type.Type,
                ["name"] = type.Name,
                ["app"] = type.App,
                ["count"] = type.Count,
            });
        }
        return new JsonObject
        {
            ["supported"] = census.Supported,
            ["grefs"] = census.GlobalRefs,
            ["weakGrefs"] = census.WeakGlobalRefs,
            ["total"] = census.Types.Sum(t => t.Count),
            ["types"] = types,
        };
    }
}
