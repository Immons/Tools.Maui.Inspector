using Microsoft.Extensions.Logging;

namespace Immons.Tools.Maui.Inspector.Shared;

/// <summary>
/// Fire-and-forget that is always observed. A discarded task that faults ends up in
/// <see cref="TaskScheduler.UnobservedTaskException"/> — which the host app's crash reporter records
/// as an unhandled error of the app. Whatever the inspector starts in the background goes through
/// here: the failure is logged (Logs view + console) and never leaves the inspector.
/// </summary>
internal static class FireAndForget
{
    public static void Run(Func<Task> work, string what) => Observe(Task.Run(work), what);

    public static void Observe(Task task, string what)
    {
        if (task.IsCompleted)
        {
            Report(task, what);
            return;
        }
        task.ContinueWith(static (t, state) => Report(t, (string)state!), what,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    static void Report(Task task, string what)
    {
        try
        {
            if (!task.IsFaulted)
                return;
            // Reading Exception marks the task observed.
            var error = task.Exception!.Flatten().InnerExceptions is { Count: > 0 } inner ? inner[0] : task.Exception;
            var message = $"background {what} failed: {error.GetType().Name}: {error.Message}";
            Console.WriteLine("[MauiInspector] " + message);
            InspectorServices.Current.Logs.Write(LogLevel.Warning, "MauiInspector", message);
        }
        catch
        {
            // logging must never throw out of a continuation
        }
    }
}
