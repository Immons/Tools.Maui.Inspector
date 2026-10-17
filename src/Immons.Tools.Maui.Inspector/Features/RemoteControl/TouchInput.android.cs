using Android.Views;
using AView = Android.Views.View;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>Android touch injection: real MotionEvents dispatched to the topmost window's decor view.</summary>
internal static partial class TouchInput
{
    private static partial bool PlatformAvailable() => true;

    private static partial ITouchSession? CreatePlatform() => new MotionEventTouch();
}

internal sealed class MotionEventTouch : ITouchSession
{
    AView? _target;
    long _downTime;
    float _density = 1;

    public bool Down(Point windowPoint)
    {
        _target = TopmostDecor();
        if (_target == null)
            return false;
        _density = _target.Resources?.DisplayMetrics?.Density ?? 1;
        _downTime = Android.OS.SystemClock.UptimeMillis();
        return Dispatch(MotionEventActions.Down, windowPoint);
    }

    public void Move(Point windowPoint) => Dispatch(MotionEventActions.Move, windowPoint);

    public void Up(Point windowPoint)
    {
        Dispatch(MotionEventActions.Up, windowPoint);
        _target = null;
    }

    bool Dispatch(MotionEventActions action, Point windowPoint)
    {
        if (_target == null)
            return false;
        try
        {
            var now = Android.OS.SystemClock.UptimeMillis();
            using var motion = MotionEvent.Obtain(_downTime, now, action,
                (float)(windowPoint.X * _density), (float)(windowPoint.Y * _density), 0);
            if (motion == null)
                return false;
            motion.SetSource(InputSourceType.Touchscreen);
            _target.DispatchTouchEvent(motion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>MAUI hosts each modal page in a Dialog window above the activity — touch the topmost.</summary>
    static AView? TopmostDecor()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var decor = activity?.Window?.DecorView;

        IReadOnlyList<Page>? modals = null;
        try { modals = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation?.ModalStack; }
        catch { /* navigation may be unavailable mid-teardown */ }

        for (var i = (modals?.Count ?? 0) - 1; i >= 0; i--)
        {
            if (modals![i].Handler?.PlatformView is AView view && view.IsAttachedToWindow && view.RootView is { } root)
                return root;
        }
        return decor;
    }
}
