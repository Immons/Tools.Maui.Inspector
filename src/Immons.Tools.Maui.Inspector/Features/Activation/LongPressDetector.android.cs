using Android.Content;
using Android.Views;

namespace Immons.Tools.Maui.Inspector.Features.Activation;

/// <summary>
/// Detects a long press (1 or 2 fingers) from raw MotionEvents without consuming them.
/// Coordinates reported are window-relative pixels.
/// </summary>
internal sealed class LongPressDetector
{
    readonly float _slopPx;
    readonly long _durationMs;
    readonly int _requiredPointers;
    readonly Action<float, float> _onLongPress;
    readonly Android.OS.Handler _handler;

    float _startX, _startY;
    bool _tracking;
    int _sequence;

    public LongPressDetector(Context context, MauiInspectorOptions options, Action<float, float> onLongPress)
    {
        _slopPx = (ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16) * 2f;
        _durationMs = (long)ActivationGate.EffectiveDuration(options.LongPressDuration).TotalMilliseconds;
        _requiredPointers = Math.Clamp(options.LongPressTouchCount, 1, 2);
        _onLongPress = onLongPress;
        _handler = new Android.OS.Handler(Android.OS.Looper.MainLooper!);
    }

    /// <summary>When true, incoming events are ignored (inspector already open).</summary>
    public bool Suspended { get; set; }

    public void OnTouchEvent(MotionEvent e)
    {
        if (Suspended)
        {
            Cancel();
            return;
        }

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                if (_requiredPointers == 1)
                    Begin(e);
                break;

            case MotionEventActions.PointerDown:
                if (e.PointerCount == _requiredPointers)
                    Begin(e);
                else if (e.PointerCount > _requiredPointers)
                    Cancel();
                break;

            case MotionEventActions.Move:
                if (_tracking && Distance(e) > _slopPx)
                    Cancel();
                break;

            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
            case MotionEventActions.Cancel:
                Cancel();
                break;
        }
    }

    void Begin(MotionEvent e)
    {
        (_startX, _startY) = Centroid(e);
        _tracking = true;
        var sequence = ++_sequence;
        _handler.PostDelayed(() =>
        {
            if (_tracking && sequence == _sequence)
            {
                _tracking = false;
                _onLongPress(_startX, _startY);
            }
        }, _durationMs);
    }

    void Cancel()
    {
        _tracking = false;
        _sequence++;
    }

    float Distance(MotionEvent e)
    {
        var (x, y) = Centroid(e);
        var dx = x - _startX;
        var dy = y - _startY;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    static (float X, float Y) Centroid(MotionEvent e)
    {
        float x = 0, y = 0;
        var count = e.PointerCount;
        for (var i = 0; i < count; i++)
        {
            x += e.GetX(i);
            y += e.GetY(i);
        }
        return (x / count, y / count);
    }
}
