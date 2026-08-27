using System.Net;
using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.Web;

/// <summary>
/// The heap-dump hand-off. Panel side: POST …/dump/request, POST …/dump/cancel, GET /api/memory/dumps.
/// Tool side (maui-inspector-sync): GET …/dump/pending, POST …/dump/status, POST …/dump/result.
/// </summary>
internal sealed class HeapDumpEndpoint(
    IHeapDumpRequests requests,
    ISnapshotRunner snapshots,
    ISyncTracker sync) : IHttpEndpoint
{
    public async Task<bool> TryHandle(HttpListenerContext context, string method, string path)
    {
        if (method == HttpVerbs.Get && path == ApiRoutes.Memory.Dumps)
        {
            // The list is polled every couple of seconds; a report is megabytes. One goes on demand.
            var jobs = new JsonArray();
            foreach (var job in requests.Jobs)
                jobs.Add(HeapDumpJsonBuilder.Job(job, includeReport: false));
            await HttpResponse.WriteJson(context, new JsonObject { ["jobs"] = jobs }.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && path == ApiRoutes.Memory.DumpReport)
        {
            var wanted = int.TryParse(context.Request.QueryString["id"], out var parsed) ? parsed : 0;
            var job = requests.Find(wanted);
            var json = new JsonObject { ["job"] = job == null ? null : HeapDumpJsonBuilder.Job(job, includeReport: true) };
            await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Post && path == ApiRoutes.Memory.HeapDump)
        {
            await OrderAndWait(context).ConfigureAwait(false);
            return true;
        }

        if (method == HttpVerbs.Get && path == ApiRoutes.Memory.DumpPending)
        {
            sync.MarkPolled();
            var pending = requests.Pending();
            var json = new JsonObject { ["job"] = pending == null ? null : HeapDumpJsonBuilder.Pending(pending, sync.Connected) };
            await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        if (method != HttpVerbs.Post)
            return false;
        if (path is not (ApiRoutes.Memory.DumpRequest or ApiRoutes.Memory.DumpStatus or ApiRoutes.Memory.DumpResult or ApiRoutes.Memory.DumpCancel
            or ApiRoutes.Memory.DumpTrace or ApiRoutes.Memory.AllocRequest))
            return false;

        var body = await RequestBody.ReadJson(context).ConfigureAwait(false);
        var id = body?["id"]?.GetValue<int>() ?? 0;

        if (path is ApiRoutes.Memory.DumpTrace or ApiRoutes.Memory.AllocRequest)
        {
            var job = path == ApiRoutes.Memory.DumpTrace
                ? requests.RequestTrace((string?)body?["type"] ?? "", body?["jobId"]?.GetValue<int>() ?? 0)
                : requests.RequestAlloc(body?["seconds"]?.GetValue<int>() ?? 10);
            var json = new JsonObject { ["ok"] = true, ["job"] = HeapDumpJsonBuilder.Job(job, includeReport: false) };
            await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
            return true;
        }
        var ok = path switch
        {
            ApiRoutes.Memory.DumpStatus => requests.Update(id, ParsePhase(body?["phase"]), (string?)body?["message"] ?? ""),
            ApiRoutes.Memory.DumpResult => CompleteChecked(id, body),
            ApiRoutes.Memory.DumpCancel => requests.Cancel(id),
            _ => false,
        };

        if (path == ApiRoutes.Memory.DumpRequest)
        {
            var job = requests.Request(RequestedTypes(body));
            var json = new JsonObject { ["ok"] = true, ["job"] = HeapDumpJsonBuilder.Job(job, includeReport: false) };
            await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
            return true;
        }

        await HttpResponse.WriteOk(context, ok).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A report has to answer the job that was asked. An older maui-inspector-sync knows only heap
    /// dumps and answers an allocation recording with one — better a clear failure than a report
    /// that means something else.
    /// </summary>
    bool CompleteChecked(int id, JsonNode? body)
    {
        var reported = (string?)body?["report"]?["kind"] ?? JobKinds.Dump;
        if (requests.Find(id) is { } job && job.Kind != reported)
        {
            return requests.Update(id, HeapDumpPhase.Failed,
                $"maui-inspector-sync answered with a '{reported}' report for a '{job.Kind}' job — the tool is older than this app; update it: dotnet tool update -g Immons.Tools.Maui.Inspector.Sync");
        }
        return requests.Complete(id, (string?)body?["file"], body?["report"]?.ToJsonString() ?? "{}");
    }

    /// <summary>
    /// One call for a script: order a heap dump and answer with the finished report. Polling a job
    /// and matching its types is the panel's business, not a shell script's — this waits (bounded)
    /// and returns what the tool found, or why it did not.
    /// </summary>
    async Task OrderAndWait(HttpListenerContext context)
    {
        var body = await RequestBody.ReadJson(context).ConfigureAwait(false);
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(body?["timeoutMs"]?.GetValue<int>() ?? DefaultWaitMs, 1000, MaxWaitMs));
        var job = requests.Request(RequestedTypes(body));

        var deadline = DateTime.UtcNow + timeout;
        while (job.IsActive && DateTime.UtcNow < deadline)
            await Task.Delay(PollMs).ConfigureAwait(false);

        var json = new JsonObject
        {
            ["ok"] = job.Phase == HeapDumpPhase.Done,
            ["waiting"] = job.IsActive,
            ["job"] = HeapDumpJsonBuilder.Job(job, includeReport: true),
        };
        if (job.IsActive)
            json["hint"] = "still running — poll GET /api/memory/dumps, then GET /api/memory/dump/report?id=" + job.Id;
        else if (job.Phase != HeapDumpPhase.Done)
            json["hint"] = job.Message;
        await HttpResponse.WriteJson(context, json.ToJsonString()).ConfigureAwait(false);
    }

    const int DefaultWaitMs = 420_000;
    const int MaxWaitMs = 900_000;
    const int PollMs = 250;

    /// <summary>Explicit types from the panel, else whatever the last snapshot flagged.</summary>
    IReadOnlyList<string> RequestedTypes(JsonNode? body)
    {
        var explicitTypes = (body?["types"] as JsonArray)?.Select(n => (string?)n).OfType<string>().ToList();
        return explicitTypes is { Count: > 0 } ? explicitTypes : SnapshotJsonBuilder.SuspectTypes(snapshots.Latest);
    }

    static HeapDumpPhase ParsePhase(JsonNode? node) =>
        Enum.TryParse<HeapDumpPhase>((string?)node, ignoreCase: true, out var phase) ? phase : HeapDumpPhase.Running;
}
