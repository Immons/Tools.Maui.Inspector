using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

internal static class HeapDumpJsonBuilder
{
    /// <summary>The job as the panel sees it — with the tool's report when there is one.</summary>
    public static JsonObject Job(HeapDumpJob job, bool includeReport)
    {
        var types = new JsonArray();
        foreach (var type in job.SuspectTypes)
            types.Add(type);
        return new JsonObject
        {
            ["id"] = job.Id,
            ["kind"] = job.Kind,
            ["sourceJob"] = job.SourceJobId == 0 ? null : job.SourceJobId,
            ["sourceFile"] = job.SourceFile,
            ["seconds"] = job.Seconds == 0 ? null : job.Seconds,
            ["requested"] = job.Requested.ToString("HH:mm:ss"),
            ["phase"] = job.Phase.ToString().ToLowerInvariant(),
            ["message"] = job.Message,
            ["finished"] = job.Finished?.ToString("HH:mm:ss"),
            ["file"] = job.File,
            ["hasReport"] = job.ReportBytes > 0,
            // What the inspector itself holds for this report — it is a memory tool, it should say.
            ["reportBytes"] = job.ReportBytes,
            ["types"] = types,
            ["report"] = includeReport && job.ReportJson != null ? JsonNode.Parse(job.ReportJson) : null,
        };
    }

    /// <summary>The job as the tool sees it — with the target and the app's assemblies for the app/framework split.</summary>
    public static JsonObject Pending(HeapDumpJob job, bool syncToolConnected)
    {
        var json = Job(job, includeReport: false);
        json["app"] = AppInfo.Current.Name;
        HeapDumpTarget.Describe(json, syncToolConnected);
        var assemblies = new JsonArray();
        var packages = new JsonArray();
        foreach (var assembly in AppAssemblies.Own())
        {
            var name = assembly.GetName().Name;
            (AppAssemblies.IsOwn(name ?? "") ? assemblies : packages).Add(name);
        }
        json["appAssemblies"] = assemblies;
        json["packageAssemblies"] = packages;
        return json;
    }
}
