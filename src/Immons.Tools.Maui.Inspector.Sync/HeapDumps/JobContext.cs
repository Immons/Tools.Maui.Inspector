using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>One job as the tool sees it, with the two ways of talking back to the app.</summary>
internal sealed class JobContext(HttpClient http, string url, JsonNode job, DiagnosticTools tools, string dumpsDirectory)
{
    public int Id { get; } = job["id"]?.GetValue<int>() ?? 0;

    public string Kind { get; } = job["kind"]?.GetValue<string>() ?? "dump";

    public DumpTarget Target { get; } = DumpTarget.From(job);

    public IReadOnlyList<string> Types { get; } = (job["types"] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>().ToList() ?? [];

    public HashSet<string> AppAssemblies { get; } = (job["appAssemblies"] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet() ?? [];

    /// <summary>Third-party packages: not the framework, not the app's own code.</summary>
    public HashSet<string> PackageAssemblies { get; } = (job["packageAssemblies"] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet() ?? [];

    public string App { get; } = job["app"]?.GetValue<string>() ?? "app";

    public string? SourceFile { get; } = job["sourceFile"]?.GetValue<string>();

    public int SourceJobId { get; } = job["sourceJob"]?.GetValue<int>() ?? 0;

    public int Seconds { get; } = job["seconds"]?.GetValue<int>() ?? 10;

    public bool AllocationTracking { get; } = job["allocTracking"]?.GetValue<bool>() ?? true;

    public DiagnosticTools Tools { get; } = tools;

    public string DumpsDirectory { get; } = dumpsDirectory;

    public string NewFile(string extension)
    {
        Directory.CreateDirectory(DumpsDirectory);
        return Path.Combine(DumpsDirectory, $"{Sanitize(App)}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");
    }

    /// <summary>This app's own router — a dump of one app never blocks a dump of another.</summary>
    public Task<(DsRouterProcess? Router, string? Problem)> StartRouter() =>
        Target.NeedsRouter
            ? DsRouterProcess.Start(Tools.DsRouterPath!, Target, url, Running)
            : Task.FromResult<(DsRouterProcess?, string?)>((null, null));

    public Task Status(string phase, string message) =>
        http.PostAsync($"{url}/api/memory/dump/status",
            new StringContent(new JsonObject { ["id"] = Id, ["phase"] = phase, ["message"] = message }.ToJsonString()));

    public Task Running(string message) => Status("running", message);

    public async Task Fail(string message)
    {
        await Status("failed", message);
        Console.WriteLine($"job #{Id} ({Kind}) failed: {message}");
    }

    /// <summary>
    /// Asks the app to collect. Mono names a vtable only while dumping the heap, which it does on a
    /// collection — so a GC late in an allocation recording is what names the types allocated during it.
    /// </summary>
    public Task ForceGc() => http.PostAsync($"{url}/api/memory/gc", new StringContent("{}"));

    public Task Result(string? file, JsonObject report) =>
        http.PostAsync($"{url}/api/memory/dump/result",
            new StringContent(new JsonObject { ["id"] = Id, ["file"] = file, ["report"] = report }.ToJsonString()));

    static string Sanitize(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-') is { Length: > 0 } s ? s : "app";
}
