namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

/// <summary>The kinds of work the desktop tool carries out for the panel.</summary>
internal static class JobKinds
{
    /// <summary>A whole heap dump: histogram plus the suspects' root paths.</summary>
    public const string Dump = "dump";

    /// <summary>Root paths of one more type, read from an existing dump file.</summary>
    public const string Trace = "trace";

    /// <summary>Allocation sampling for a few seconds: which types allocate how much.</summary>
    public const string Alloc = "alloc";
}

/// <summary>
/// One job ordered from the panel and carried out by the desktop tool: the app only keeps the
/// order, the progress the tool reports and the report it posts back.
/// </summary>
internal sealed class HeapDumpJob(int id, DateTime requested, IReadOnlyList<string> suspectTypes)
{
    public int Id { get; } = id;

    public DateTime Requested { get; } = requested;

    public string Kind { get; init; } = JobKinds.Dump;

    /// <summary>Types the tool should trace to their GC roots — the snapshot's suspects, or the one type of a trace job.</summary>
    public IReadOnlyList<string> SuspectTypes { get; } = suspectTypes;

    /// <summary>Trace jobs: the dump file (on the desktop) to read; and the job it came from.</summary>
    public string? SourceFile { get; init; }

    public int SourceJobId { get; init; }

    /// <summary>Alloc jobs: how long to sample.</summary>
    public int Seconds { get; init; }

    public HeapDumpPhase Phase { get; set; } = HeapDumpPhase.Pending;

    public string Message { get; set; } = "";

    /// <summary>Last word from the tool — a job nobody reports on for minutes has been abandoned.</summary>
    public DateTime LastUpdate { get; set; } = requested;

    public DateTime? Finished { get; set; }

    /// <summary>The .gcdump / .nettrace on the desktop, for Visual Studio / PerfView.</summary>
    public string? File { get; set; }

    /// <summary>
    /// The tool's report. A heap dump of a real app is megabytes of JSON and three of them stay
    /// around for comparison, so it is held compressed — the panel asks for it rarely, and the
    /// inspector has no business being the app's biggest allocation.
    /// </summary>
    public string? ReportJson
    {
        get => _report == null ? null : ReportStore.Unpack(_report);
        set => _report = value == null ? null : ReportStore.Pack(value);
    }

    /// <summary>Bytes actually held for the report — what the Memory view would blame the inspector for.</summary>
    public int ReportBytes => _report?.Length ?? 0;

    byte[]? _report;

    public bool IsActive => Phase is HeapDumpPhase.Pending or HeapDumpPhase.Running;
}
