using System.Diagnostics;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// Installs a .NET global tool into the inspector's private tool folder (`dotnet tool install
/// --tool-path`), so nothing of the user's own global tools changes. The first attempt honours the
/// machine's own feeds — an internal mirror may be all the user can reach. A private feed that
/// answers 401 kills the whole install though (`--ignore-failed-sources` does not save it: NuGet
/// still throws), so the retry replaces every source with nuget.org, where the diagnostic tools
/// live. `--source` replaces, `--add-source` only appends — appending would keep the broken feed.
/// </summary>
internal static class ToolInstaller
{
    static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(4);

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-inspector", "tools");

    /// <summary>Installs (or updates, when already present in the folder) the tool; null on success, else why not.</summary>
    public static async Task<string?> Install(string toolId, bool update)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var verb = update ? "update" : "install";
        var baseArguments = $"tool {verb} {toolId} --tool-path \"{Directory}\"";

        var first = await Run($"{baseArguments} --ignore-failed-sources");
        if (first == null)
            return null;

        var second = await Run($"{baseArguments} --source {NugetOrg}");
        return second == null ? null : $"{first} / {second}";
    }

    /// <summary>Runs `dotnet …`; null when it exits 0, else the last line it printed.</summary>
    static async Task<string?> Run(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi);
        if (process == null)
            return "could not start dotnet";

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(InstallTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return $"dotnet {arguments} did not finish within {InstallTimeout.TotalMinutes:F0} minutes";
        }

        if (process.ExitCode == 0)
            return null;
        var lines = (await output + "\n" + await error).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault() ?? $"exit code {process.ExitCode}";
    }

    const string NugetOrg = "https://api.nuget.org/v3/index.json";
}
