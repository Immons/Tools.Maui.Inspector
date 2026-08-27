using System.Diagnostics;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>Runs one diagnostic tool to completion, streaming its lines as progress; a hard timeout kills it.</summary>
internal static class ToolRunner
{
    public static async Task<(bool Ok, string Message)> Run(string toolPath, string arguments, string outputFile, TimeSpan timeout, Func<string, Task> progress)
    {
        var psi = new ProcessStartInfo(toolPath, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(psi);
        if (process == null)
            return (false, $"could not start {Path.GetFileName(toolPath)}");

        var lines = new List<string>();
        var stdout = Pump(process.StandardOutput, lines, progress);
        var stderr = Pump(process.StandardError, lines, progress);

        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }
            return (false, $"{Path.GetFileName(toolPath)} did not finish within {timeout.TotalSeconds:F0} s — is the app running with the diagnostic port?");
        }
        await Task.WhenAll(stdout, stderr);

        if (process.ExitCode != 0 || !File.Exists(outputFile))
            return (false, Summary(lines) is { Length: > 0 } summary ? summary : $"{Path.GetFileName(toolPath)} exited with {process.ExitCode}");
        return (true, Summary(lines));
    }

    static async Task Pump(StreamReader reader, List<string> lines, Func<string, Task> progress)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            lock (lines)
            {
                lines.Add(trimmed);
            }
            try
            {
                await progress(trimmed);
            }
            catch
            {
                // progress is best effort
            }
        }
    }

    /// <summary>The last meaningful lines — the tool's own words on what happened.</summary>
    static string Summary(List<string> lines)
    {
        lock (lines)
        {
            return string.Join(" · ", lines.Where(l => !l.StartsWith("dotnet build", StringComparison.Ordinal)).TakeLast(3));
        }
    }
}
