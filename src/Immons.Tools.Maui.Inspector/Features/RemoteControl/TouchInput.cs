namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// One synthesized finger: down, any number of moves, up — delivered through the platform's own
/// touch pipeline (hit-test, gesture recognizers, controls), exactly where a real finger lands.
/// All calls on the main thread.
/// </summary>
internal interface ITouchSession
{
    /// <summary>False when nothing at the point accepts touches (or injection failed).</summary>
    bool Down(Point windowPoint);

    void Move(Point windowPoint);

    void Up(Point windowPoint);
}

/// <summary>Platform touch injection: iOS synthesizes UITouch events, Android dispatches MotionEvents.</summary>
internal static partial class TouchInput
{
    /// <summary>True when this platform can inject touches and the app did not switch it off.</summary>
    public static bool Available => MauiInspector.Options.NativeTouchInjection && PlatformAvailable();

    /// <summary>A new finger, or null when injection is unavailable. Main thread.</summary>
    public static ITouchSession? Start() => Available ? CreatePlatform() : null;

    private static partial bool PlatformAvailable();

    private static partial ITouchSession? CreatePlatform();
}

/// <summary>
/// Runs gestures as timed sequences of touch events. Each event is its own main-thread hop, so
/// the app's run loop (recognizer timers, animations, scroll physics) runs between them — the
/// way it does under a real finger.
/// </summary>
internal sealed class GestureRunner(IMainThreadDispatcher mainThread)
{
    /// <summary>A tap held long enough for highlight states, short of any long-press threshold.</summary>
    public const int TapHoldMs = 60;

    public Task<bool> Tap(Point point) => Press(point, TapHoldMs);

    public Task<bool> LongPress(Point point, int durationMs) => Press(point, Math.Max(durationMs, 100));

    public async Task<bool> Press(Point point, int holdMs)
    {
        var session = await mainThread.RunAsync(() =>
        {
            var touch = TouchInput.Start();
            return touch != null && touch.Down(point) ? touch : null;
        }).ConfigureAwait(false);
        if (session == null)
            return false;

        await Task.Delay(holdMs).ConfigureAwait(false);
        await mainThread.RunAsync(() =>
        {
            session.Up(point);
            return true;
        }).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Down at <paramref name="from"/>, moves to <paramref name="to"/> over the duration, up.
    /// A short hold at the end (<paramref name="holdEndMs"/>) stops a fling — useful for drags.
    /// </summary>
    public async Task<bool> Swipe(Point from, Point to, int durationMs, int holdEndMs = 0)
    {
        var session = await mainThread.RunAsync(() =>
        {
            var touch = TouchInput.Start();
            return touch != null && touch.Down(from) ? touch : null;
        }).ConfigureAwait(false);
        if (session == null)
            return false;

        foreach (var step in GesturePath.Interpolate(new PathPoint(from.X, from.Y), new PathPoint(to.X, to.Y), durationMs))
        {
            await Task.Delay(GesturePath.StepMs).ConfigureAwait(false);
            var point = new Point(step.X, step.Y);
            await mainThread.RunAsync(() =>
            {
                session.Move(point);
                return true;
            }).ConfigureAwait(false);
        }

        if (holdEndMs > 0)
            await Task.Delay(holdEndMs).ConfigureAwait(false);

        await mainThread.RunAsync(() =>
        {
            session.Up(to);
            return true;
        }).ConfigureAwait(false);
        return true;
    }
}
