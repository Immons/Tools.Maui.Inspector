namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

/// <summary>The hand-off queue between the panel and the desktop tool.</summary>
internal interface IHeapDumpRequests
{
    /// <summary>Orders a dump, or returns the job already in progress.</summary>
    HeapDumpJob Request(IReadOnlyList<string> suspectTypes);

    /// <summary>Orders the root paths of one more type from an already collected dump.</summary>
    HeapDumpJob RequestTrace(string type, int sourceJobId);

    /// <summary>Orders allocation sampling for the given number of seconds.</summary>
    HeapDumpJob RequestAlloc(int seconds);

    /// <summary>The job waiting for a tool to pick it up, if any.</summary>
    HeapDumpJob? Pending();

    HeapDumpJob? Active { get; }

    /// <summary>Newest first, bounded.</summary>
    IReadOnlyList<HeapDumpJob> Jobs { get; }

    HeapDumpJob? Find(int id);

    bool Update(int id, HeapDumpPhase phase, string message);

    bool Complete(int id, string? file, string reportJson);

    bool Cancel(int id);
}
