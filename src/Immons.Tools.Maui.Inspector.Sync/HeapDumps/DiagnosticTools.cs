using System.Diagnostics;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// Locates dotnet-gcdump and dotnet-dsrouter and installs what is missing or too old (the
/// --dsrouter option that reaches Android/iOS arrived in gcdump 9.0.652701) into the inspector's
/// private tool folder, leaving the user's global tools alone. gcdump starts dsrouter strictly from
/// its own folder, so a private gcdump always comes with a private dsrouter next to it.
/// </summary>
internal sealed class DiagnosticTools
{
    const string GcDump = "dotnet-gcdump";
    const string DsRouter = "dotnet-dsrouter";
    const string Trace = "dotnet-trace";
    static readonly Version MinimumGcDump = new(9, 0, 652701);

    public static bool AutoInstall { get; set; } = true;

    public string? GcDumpPath { get; private set; }

    public string? DsRouterPath { get; private set; }

    public string GcDumpVersion { get; private set; } = "";

    /// <summary>What is still missing for the target after trying to install it, or null when everything is in place.</summary>
    public async Task<string?> Check(DumpTarget target, Func<string, Task> progress)
    {
        var gcdump = Locate(GcDump);
        if (gcdump == null || !await IsRecent(gcdump))
        {
            var manual = gcdump == null
                ? $"{GcDump} is not installed — run: dotnet tool install -g {GcDump}"
                : $"{GcDump} at {gcdump} is too old for --dsrouter (needs {MinimumGcDump}) — run: dotnet tool update -g {GcDump}";
            if (await InstallPrivately(progress, GcDump, target.NeedsRouter ? DsRouter : null) is { } problem)
                return $"{manual} (automatic install failed: {problem})";
            gcdump = PrivatePath(GcDump);
        }

        if (target.NeedsRouter)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(gcdump)!, ToolFileName(DsRouter));
            if (!File.Exists(sibling))
            {
                // The router is started by this tool, but it must exist somewhere; keep the pair together.
                if (await InstallPrivately(progress, GcDump, DsRouter) is { } problem)
                    return $"{DsRouter} is not installed — run: dotnet tool install -g {DsRouter} (automatic install failed: {problem})";
                gcdump = PrivatePath(GcDump);
                sibling = PrivatePath(DsRouter);
            }
            DsRouterPath = sibling;
        }

        GcDumpPath = gcdump;
        GcDumpVersion = await Version(gcdump);
        return null;
    }

    public string? TracePath { get; private set; }

    /// <summary>dotnet-trace for allocation sampling: the same private-install rule, the same sibling dsrouter.</summary>
    public async Task<string?> CheckTrace(DumpTarget target, Func<string, Task> progress)
    {
        if (await Check(target, progress) is { } problem)
            return problem;
        var trace = Locate(Trace);
        if (trace == null || !await IsRecent(trace))
        {
            if (await InstallPrivately(progress, Trace, DsRouter) is { } installProblem)
                return $"{Trace} is not installed — run: dotnet tool install -g {Trace} (automatic install failed: {installProblem})";
            trace = PrivatePath(Trace);
        }
        if (target.NeedsRouter && !File.Exists(Path.Combine(Path.GetDirectoryName(trace)!, ToolFileName(DsRouter))))
        {
            if (await InstallPrivately(progress, Trace, DsRouter) is { } installProblem)
                return $"{DsRouter} next to {Trace} is missing (automatic install failed: {installProblem})";
            trace = PrivatePath(Trace);
        }
        TracePath = trace;
        return null;
    }

    public string Describe() => $"{GcDump} {GcDumpVersion}";

    /// <summary>Human-readable inventory for the `tools` verb and the logs.</summary>
    public string Inventory() =>
        $"{GcDump}: {GcDumpPath ?? "missing"} ({GcDumpVersion})" + (DsRouterPath == null ? "" : $" · {DsRouter}: {DsRouterPath}");

    static async Task<string?> InstallPrivately(Func<string, Task> progress, params string?[] tools)
    {
        if (!AutoInstall)
            return "automatic installation is disabled (--no-tool-install)";
        foreach (var tool in tools.OfType<string>())
        {
            var present = File.Exists(PrivatePath(tool));
            if (present && tool == DsRouter)
                continue;
            await progress($"{(present ? "updating" : "installing")} {tool} in {ToolInstaller.Directory} — a moment…");
            if (await ToolInstaller.Install(tool, update: present) is { } problem)
                return problem;
        }
        return null;
    }

    static string PrivatePath(string tool) => Path.Combine(ToolInstaller.Directory, ToolFileName(tool));

    static string ToolFileName(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    /// <summary>The private folder first — it is the one whose version this class keeps current.</summary>
    static string? Locate(string tool)
    {
        var name = ToolFileName(tool);
        var candidates = new[] { ToolInstaller.Directory }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"));
        return candidates.Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
    }

    /// <summary>A tool that cannot even say its version (a stale shim, a broken install) counts as missing.</summary>
    static async Task<bool> IsRecent(string gcdumpPath)
    {
        var version = await Version(gcdumpPath);
        return System.Version.TryParse(version.Split('+', '-')[0], out var parsed) && parsed >= MinimumGcDump;
    }

    static async Task<string> Version(string path)
    {
        try
        {
            var psi = new ProcessStartInfo(path, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(psi);
            if (process == null)
                return "";
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
