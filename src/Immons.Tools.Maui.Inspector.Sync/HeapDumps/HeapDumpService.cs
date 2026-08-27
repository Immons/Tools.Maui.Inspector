using System.Text.Json.Nodes;

namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>
/// The desktop half of the Memory view's hand-offs. Polls each app for a pending job — a heap
/// dump, a trace of one type in an existing dump, an allocation recording — carries it out and
/// posts the report back; the panel renders it. One job at a time per app; the XAML polling
/// keeps running meanwhile.
/// </summary>
internal sealed class HeapDumpService(HttpClient http, string dumpsDirectory)
{
    readonly DiagnosticTools _tools = new();
    readonly HashSet<string> _running = [];

    public async Task Poll(string url)
    {
        lock (_running)
        {
            if (_running.Contains(url))
                return;
        }

        JsonNode? job;
        try
        {
            job = JsonNode.Parse(await http.GetStringAsync($"{url}/api/memory/dump/pending"))?["job"];
        }
        catch
        {
            return; // an older inspector without the Memory view, or the app is away
        }
        if (job is not JsonObject)
            return;

        lock (_running)
        {
            _running.Add(url);
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await JobRunners.Run(new JobContext(http, url, job, _tools, dumpsDirectory));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"job failed ({url}): {ex.Message}");
            }
            finally
            {
                lock (_running)
                {
                    _running.Remove(url);
                }
            }
        });
    }
}
