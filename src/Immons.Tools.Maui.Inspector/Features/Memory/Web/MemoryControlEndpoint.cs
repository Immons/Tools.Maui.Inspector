using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Web;

/// <summary>
/// POST /api/memory/settings — watch mode and the bisection aids; GET /api/memory/ledger — the
/// navigation ledger; GET /api/memory/snapshots — the history digests; GET /api/memory/images —
/// the decoded images (main thread: it reads platform views).
/// </summary>
internal sealed class MemoryControlEndpoint(
    IMainThreadDispatcher mainThread,
    INavigationLedger ledger,
    ISnapshotRunner snapshots,
    ITrackedInstances instances,
    IInstanceTracker tracker) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Post && path == ApiRoutes.Memory.Settings)
        {
            // Reading the visual tree again on the way back on belongs to the main thread.
            var body = await RequestBody.ReadJson(context).ConfigureAwait(false);
            if (body?["tracking"]?.GetValue<bool>() is { } tracking)
                await mainThread.RunAsync(() => { tracker.SetEnabled(tracking); return true; }).ConfigureAwait(false);
            Apply(body);
            await HttpResponse.WriteJson(context, MemorySettingsJson.Build().ToJsonString()).ConfigureAwait(false);
            return true;
        }
        if (method != HttpVerbs.Get)
            return false;

        switch (path)
        {
            case ApiRoutes.Memory.Ledger:
                await HttpResponse.WriteJson(context, LedgerJson().ToJsonString()).ConfigureAwait(false);
                return true;
            case ApiRoutes.Memory.Snapshots:
                await HttpResponse.WriteJson(context, HistoryJson().ToJsonString()).ConfigureAwait(false);
                return true;
            case ApiRoutes.Memory.Images:
                var report = await mainThread.RunAsync(() => ImageCensus.Collect(instances)).ConfigureAwait(false);
                await HttpResponse.WriteJson(context, ImagesJson(report).ToJsonString()).ConfigureAwait(false);
                return true;
        }
        return false;
    }

    static void Apply(JsonNode? body)
    {
        var options = MauiInspector.Options.Memory;
        // Watch mode is snapshots, and snapshots need the registry — it cannot outlive tracking.
        if (body?["watch"]?.GetValue<bool>() is { } watch)
            options.WatchNavigation = watch && options.TrackInstances;
        if (body?["disconnectHandlersOnPop"]?.GetValue<bool>() is { } disconnect)
            options.DisconnectHandlersOnPop = disconnect;
        if (body?["clearBindingContextOnPop"]?.GetValue<bool>() is { } clear)
            options.ClearBindingContextOnPop = clear;
    }

    JsonObject LedgerJson()
    {
        var entries = new JsonArray();
        foreach (var e in ledger.Entries)
        {
            entries.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["type"] = e.Type,
                ["label"] = e.Label,
                ["pushed"] = e.PushedAt.ToString("HH:mm:ss"),
                ["popped"] = e.PoppedAt?.ToString("HH:mm:ss"),
                ["verdict"] = e.Verdict.ToString().ToLowerInvariant(),
                ["reported"] = e.Reported,
                ["survived"] = e.Survived,
                ["managedDelta"] = e.ManagedAtPop is { } m ? m - e.ManagedAtPush : null,
                ["processDelta"] = e.ProcessAtPop is { } p && e.ProcessAtPush is { } q ? p - q : null,
            });
        }
        var (open, pending, alive, cycles) = ledger.Counts;
        return new JsonObject
        {
            ["entries"] = entries,
            ["open"] = open,
            ["pending"] = pending,
            ["alive"] = alive,
            ["cycles"] = cycles,
            ["settings"] = MemorySettingsJson.Build(),
        };
    }

    JsonObject HistoryJson()
    {
        var list = new JsonArray();
        foreach (var d in snapshots.History)
        {
            var types = new JsonObject();
            foreach (var (type, count) in d.DetachedByType)
                types[type] = count;
            list.Add(new JsonObject { ["time"] = d.Time.ToString("HH:mm:ss"), ["alive"] = d.Alive, ["detached"] = d.Detached, ["types"] = types });
        }
        return new JsonObject { ["snapshots"] = list };
    }

    static JsonObject ImagesJson(ImageReport report)
    {
        var images = new JsonArray();
        foreach (var i in report.Images)
        {
            images.Add(new JsonObject
            {
                ["owner"] = i.Owner,
                ["source"] = i.Source,
                ["width"] = i.Width,
                ["height"] = i.Height,
                ["bytes"] = i.Bytes,
                ["attached"] = i.Attached,
            });
        }
        return new JsonObject { ["supported"] = report.Supported, ["total"] = report.Images.Count, ["bytes"] = report.TotalBytes, ["images"] = images };
    }
}
