namespace Immons.Tools.Maui.Inspector.Sync.HeapDumps;

/// <summary>The three jobs the panel can order, each a few steps of tool, file and report.</summary>
internal static class JobRunners
{
    public static Task Run(JobContext job) => job.Kind switch
    {
        "trace" => Trace(job),
        "alloc" => Alloc(job),
        _ => Dump(job),
    };

    static async Task Dump(JobContext job)
    {
        Console.WriteLine($"heap dump #{job.Id} requested ({job.Target.Describe()})");
        if (await job.Tools.Check(job.Target, job.Running) is { } missing)
        {
            await job.Fail(missing);
            return;
        }

        var file = job.NewFile("gcdump");
        await job.Running($"{job.Tools.Describe()} → {job.Target.Describe()}: waiting for the app's runtime to connect…");
        // dsrouter's adb port forwarding wipes the forwards the panel and this tool reach the app through.
        var forwards = job.Target.Platform == "android" ? await AdbForwarder.Snapshot() : [];
        var (router, routerProblem) = await job.StartRouter();
        using var routerScope = router;
        if (routerProblem != null)
        {
            await job.Fail(routerProblem);
            return;
        }
        var (ok, message) = await GcDumpRunner.Collect(job.Tools.GcDumpPath!, job.Target, router, file, job.Running);
        if (await AdbForwarder.Restore(forwards) is > 0 and var restored)
            Console.WriteLine($"heap dump #{job.Id}: restored {restored} adb forward(s) dsrouter had dropped");
        if (!ok)
        {
            await job.Fail(message);
            return;
        }

        await job.Running("reading the dump…");
        var dump = GcDumpReader.Read(file);
        if (GcDumpReader.WrongProcess(dump, job.Target) is { } wrongProcess)
        {
            await job.Fail(wrongProcess);
            return;
        }
        var report = HeapReport.Build(dump.MemoryGraph, job.Types, job.AppAssemblies, job.PackageAssemblies, job.Tools.Describe(), file);
        if (HeapReport.LacksAppTypes(report, job.AppAssemblies) is { } foreignHeap)
        {
            await job.Fail(foreignHeap);
            return;
        }
        await job.Result(file, report);
        Console.WriteLine($"heap dump #{job.Id}: {dump.MemoryGraph.NodeIndexLimit} objects, {dump.MemoryGraph.TotalSize / 1024 / 1024} MB → {file}");
    }

    static async Task Trace(JobContext job)
    {
        var type = job.Types.FirstOrDefault() ?? "";
        Console.WriteLine($"trace #{job.Id}: {type} in {job.SourceFile}");
        if (job.SourceFile == null || !File.Exists(job.SourceFile))
        {
            await job.Fail("the dump file is gone — take a new heap dump");
            return;
        }
        await job.Running($"reading {Path.GetFileName(job.SourceFile)}…");
        var dump = GcDumpReader.Read(job.SourceFile);
        await job.Result(job.SourceFile, HeapReport.BuildTrace(dump.MemoryGraph, type, job.SourceJobId, job.Tools.Describe(), job.SourceFile));
    }

    /// <summary>
    /// A collection near the end of the recording, so the heap dump it triggers names the vtables of
    /// what was allocated meanwhile. Best effort — a recording without it just has unnamed rows.
    /// </summary>
    static async Task NameTypesLate(JobContext job)
    {
        if (!job.Target.IsMono)
            return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(job.Seconds - 2, 1)));
            await job.ForceGc();
        }
        catch
        {
            // the app may be busy or gone; the report is still readable
        }
    }

    static async Task Alloc(JobContext job)
    {
        Console.WriteLine($"allocation recording #{job.Id}: {job.Seconds} s ({job.Target.Describe()})");
        if (job.Target.IsMono && !job.AllocationTracking)
        {
            var android = job.Target.Platform == "android";
            await job.Fail("this app was started without Mono's allocation profiler (MONO_DIAGNOSTICS=--diagnostic-mono-profiler=alloc), which cannot be turned on later — "
                + "update Immons.Tools.Maui.Inspector.Diagnostics (it sets this for Debug builds by itself) or remove <MauiInspectorAllocationTracking>false</MauiInspectorAllocationTracking>, then rebuild"
                + (android
                    ? ". Without a rebuild: adb shell setprop debug.mono.env 'MONO_DIAGNOSTICS=--diagnostic-mono-profiler=alloc' and restart the app"
                    : ""));
            return;
        }
        if (await job.Tools.CheckTrace(job.Target, job.Running) is { } missing)
        {
            await job.Fail(missing);
            return;
        }

        var file = job.NewFile("nettrace");
        await job.Running($"dotnet-trace → {job.Target.Describe()}: recording allocations for {job.Seconds} s — use the app now…");
        var forwards = job.Target.Platform == "android" ? await AdbForwarder.Snapshot() : [];
        var (router, routerProblem) = await job.StartRouter();
        using var routerScope = router;
        if (routerProblem != null)
        {
            await job.Fail(routerProblem);
            return;
        }
        var naming = NameTypesLate(job);
        var (ok, message) = await AllocationRunner.Record(job.Tools.TracePath!, job.Target, router, job.Seconds, file, job.Running);
        await naming;
        await AdbForwarder.Restore(forwards);
        if (!ok)
        {
            await job.Fail(message);
            return;
        }
        await job.Running("reading the trace…");
        await job.Result(file, AllocationRunner.Report(file, job.Seconds, job.Target.IsMono, job.AppAssemblies, job.PackageAssemblies, "dotnet-trace"));
        Console.WriteLine($"allocation recording #{job.Id} → {file}");
    }
}
