namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

internal sealed class HeapDumpRequests : IHeapDumpRequests
{
    /// <summary>Reports weigh a few hundred KB each; three is enough for a before/after/after comparison.</summary>
    const int KeptJobs = 3;

    /// <summary>A tool that dies mid-dump (or loses the adb forward) never reports back; its job must not block the next order.</summary>
    static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(3);

    readonly object _gate = new();
    readonly List<HeapDumpJob> _jobs = [];
    int _next;

    public HeapDumpJob Request(IReadOnlyList<string> suspectTypes) =>
        Enqueue(id => new HeapDumpJob(id, DateTime.Now, suspectTypes));

    public HeapDumpJob RequestTrace(string type, int sourceJobId) =>
        Enqueue(id => new HeapDumpJob(id, DateTime.Now, [type])
        {
            Kind = JobKinds.Trace,
            SourceJobId = sourceJobId,
            SourceFile = Find(sourceJobId)?.File,
        });

    public HeapDumpJob RequestAlloc(int seconds) =>
        Enqueue(id => new HeapDumpJob(id, DateTime.Now, []) { Kind = JobKinds.Alloc, Seconds = Math.Clamp(seconds, 1, 120) });

    public HeapDumpJob? Find(int id)
    {
        lock (_gate)
        {
            return _jobs.FirstOrDefault(j => j.Id == id);
        }
    }

    /// <summary>One job at a time; an abandoned one gives way. Dump reports stay for the comparison, the rest is bounded too.</summary>
    HeapDumpJob Enqueue(Func<int, HeapDumpJob> create)
    {
        lock (_gate)
        {
            if (_jobs.FirstOrDefault(j => j.IsActive) is { } active)
            {
                if (DateTime.Now - active.LastUpdate < AbandonedAfter)
                    return active;
                active.Phase = HeapDumpPhase.Failed;
                active.Message = "no word from maui-inspector-sync for minutes — is it running and connected to this app?";
                active.Finished = DateTime.Now;
            }

            var job = create(++_next);
            _jobs.Insert(0, job);
            foreach (var kind in new[] { JobKinds.Dump, JobKinds.Trace, JobKinds.Alloc })
            {
                foreach (var stale in _jobs.Where(j => j.Kind == kind).Skip(KeptJobs).ToList())
                    _jobs.Remove(stale);
            }
            return job;
        }
    }

    public HeapDumpJob? Pending()
    {
        lock (_gate)
        {
            return _jobs.FirstOrDefault(j => j.Phase == HeapDumpPhase.Pending);
        }
    }

    public HeapDumpJob? Active
    {
        get
        {
            lock (_gate)
            {
                return _jobs.FirstOrDefault(j => j.IsActive);
            }
        }
    }

    public IReadOnlyList<HeapDumpJob> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.ToList();
            }
        }
    }

    public bool Update(int id, HeapDumpPhase phase, string message) => Mutate(id, job =>
    {
        job.Phase = phase;
        job.Message = message;
        job.LastUpdate = DateTime.Now;
        if (!job.IsActive)
            job.Finished = DateTime.Now;
    });

    public bool Complete(int id, string? file, string reportJson) => Mutate(id, job =>
    {
        job.Phase = HeapDumpPhase.Done;
        job.Message = "";
        job.File = file;
        job.ReportJson = reportJson;
        job.LastUpdate = DateTime.Now;
        job.Finished = DateTime.Now;
    });

    public bool Cancel(int id) => Mutate(id, job =>
    {
        if (!job.IsActive)
            return;
        job.Phase = HeapDumpPhase.Failed;
        job.Message = "cancelled";
        job.Finished = DateTime.Now;
    });

    bool Mutate(int id, Action<HeapDumpJob> change)
    {
        lock (_gate)
        {
            if (_jobs.FirstOrDefault(j => j.Id == id) is not { } job)
                return false;
            change(job);
            return true;
        }
    }
}
