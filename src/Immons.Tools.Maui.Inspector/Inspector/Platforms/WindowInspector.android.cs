using Android.App;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;

namespace Immons.Tools.Maui.Inspector.Inspector;

internal sealed partial class WindowInspector
{
    Activity? _activity;
    WindowCallbackInterceptor? _interceptor;
    AView? _highlightPlatform;
    AView? _panelPlatform;

    private partial void AttachPlatform(bool activationGesture)
    {
        _activity = _window.Handler?.PlatformView as Activity;
        var platformWindow = _activity?.Window;
        if (!activationGesture || _activity == null || platformWindow?.Callback == null || platformWindow.Callback is WindowCallbackInterceptor)
            return;

        var detector = new LongPressDetector(_activity, _options, OnLongPressPx);
        _interceptor = new WindowCallbackInterceptor(platformWindow.Callback, detector);
        platformWindow.Callback = _interceptor;
    }

    private partial void DetachPlatform()
    {
        var platformWindow = _activity?.Window;
        if (platformWindow != null && _interceptor != null && ReferenceEquals(platformWindow.Callback, _interceptor))
            platformWindow.Callback = _interceptor.Wrapped;
        _interceptor = null;
        _activity = null;
    }

    void OnLongPressPx(float xPx, float yPx)
    {
        var d = Density;
        OnLongPressDetected(new Point(xPx / d, yPx / d));
    }

    double Density => _activity?.Resources?.DisplayMetrics?.Density ?? 1;

    /// <summary>
    /// The view group the overlay layers live in. MAUI hosts each modal page in its own
    /// platform window (a Dialog) stacked above the activity, so layers pinned to the
    /// activity's decor would render — and hit-test — beneath an open modal. The host is
    /// therefore the decor of the topmost modal's window, falling back to the activity.
    /// </summary>
    ViewGroup? HostDecor()
    {
        var decor = _activity?.Window?.DecorView as ViewGroup;

        IReadOnlyList<Page>? modals = null;
        try { modals = _window.Page?.Navigation?.ModalStack; }
        catch { /* navigation may be unavailable mid-teardown */ }

        for (var i = (modals?.Count ?? 0) - 1; i >= 0; i--)
        {
            if (modals![i].Handler?.PlatformView is AView view
                && view.IsAttachedToWindow
                && view.RootView is ViewGroup modalRoot
                && !ReferenceEquals(modalRoot, decor))
                return modalRoot;
        }

        return decor;
    }

    private partial void AddLayersPlatform()
    {
        if (HostDecor() is not { } decor || _mauiContext == null)
            return;

        // Keep long-press detection alive in remote highlight-only mode (no panel).
        if (_interceptor != null)
            _interceptor.Detector.Suspended = _panelLayer != null;

        _highlightPlatform = _highlightLayer!.ToPlatform(_mauiContext);
        decor.AddView(_highlightPlatform, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        if (_panelLayer == null)
            return; // remote highlight-only mode

        _panelPlatform = _panelLayer.ToPlatform(_mauiContext);
        var height = (int)(decor.Height * _options.PanelHeightFraction);
        if (height <= 0)
            height = (int)(400 * Density);
        decor.AddView(_panelPlatform, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, height) { Gravity = GravityFlags.Bottom });
        _panelPlatform.Clickable = true;
        _panelPlatform.Focusable = true;
        SetPanelOffsetPlatform(_panelLayer.DragOffsetX, _panelLayer.DragOffsetY);
    }

    private partial void RemoveLayersPlatform()
    {
        if (_interceptor != null)
            _interceptor.Detector.Suspended = false;

        if (_highlightPlatform?.Parent is ViewGroup p1)
            p1.RemoveView(_highlightPlatform);
        if (_panelPlatform?.Parent is ViewGroup p2)
            p2.RemoveView(_panelPlatform);
        _highlightPlatform = null;
        _panelPlatform = null;
    }

    private partial void SetPanelOffsetPlatform(double xDp, double yDp)
    {
        if (_panelPlatform == null)
            return;
        var d = (float)Density;
        _panelPlatform.TranslationX = (float)xDp * d;
        _panelPlatform.TranslationY = (float)yDp * d;
    }

    // Screen (not window) coordinates throughout: elements and overlay layers can live in
    // different platform windows (activity vs modal dialog), and screen space is the only
    // frame they share.
    private partial Rect? GetRectInWindowPlatform(VisualElement element)
    {
        if (element.Handler?.PlatformView is not AView pv || !pv.IsAttachedToWindow)
            return null;

        var loc = new int[2];
        pv.GetLocationOnScreen(loc);
        var d = Density;
        return new Rect(loc[0] / d, loc[1] / d, pv.Width / d, pv.Height / d);
    }

    private partial Point GetLayerOriginPlatform()
    {
        if (_highlightPlatform == null || !_highlightPlatform.IsAttachedToWindow)
            return Point.Zero;

        var loc = new int[2];
        _highlightPlatform.GetLocationOnScreen(loc);
        var d = Density;
        return new Point(loc[0] / d, loc[1] / d);
    }

    /// <summary>
    /// Composites the activity window and every modal window into one PNG. Essentials'
    /// Screenshot captures only the activity window, so with a modal open the mirror
    /// would show the page underneath it. Null when no modal is up — the regular
    /// screenshot path is both correct and cheaper then.
    /// Each window is captured via PixelCopy (the real GPU frame); software Draw is only
    /// a fallback, as it misses hardware-rendered content (render-node animations,
    /// surface-backed views), which showed up as elements missing from the mirror.
    /// </summary>
    private partial byte[]? CapturePngPlatform()
    {
        if (_activity?.Window?.DecorView is not ViewGroup decor || decor.Width <= 0 || decor.Height <= 0)
            return null;

        IReadOnlyList<Page>? modals = null;
        try { modals = _window.Page?.Navigation?.ModalStack; }
        catch { /* navigation may be unavailable mid-teardown */ }
        if (modals == null || modals.Count == 0)
            return null;

        using var bitmap = Android.Graphics.Bitmap.CreateBitmap(
            decor.Width, decor.Height, Android.Graphics.Bitmap.Config.Argb8888!);
        var canvas = new Android.Graphics.Canvas(bitmap);
        var origin = new int[2];
        decor.GetLocationOnScreen(origin);
        CaptureWindow(_activity.Window, decor, canvas, 0, 0);

        var dialogWindows = DialogWindows();
        var drawn = new HashSet<AView> { decor };
        foreach (var modal in modals)
        {
            if (modal.Handler?.PlatformView is not AView view || !view.IsAttachedToWindow)
                continue;
            var root = view.RootView;
            if (root == null || !drawn.Add(root))
                continue;

            var loc = new int[2];
            root.GetLocationOnScreen(loc);
            dialogWindows.TryGetValue(root, out var dialogWindow);
            CaptureWindow(dialogWindow, root, canvas, loc[0] - origin[0], loc[1] - origin[1]);
        }

        using var stream = new MemoryStream();
        // JPEG: the mirror refreshes continuously and PNG encoding of a full screen on the
        // UI thread is exactly the kind of stall the mirror must not cause.
        bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 80, stream);
        return stream.ToArray();
    }

    /// <summary>Modal pages live in DialogFragments; their windows keyed by decor view.</summary>
    Dictionary<AView, Android.Views.Window> DialogWindows()
    {
        var map = new Dictionary<AView, Android.Views.Window>();
        if (_activity is not AndroidX.Fragment.App.FragmentActivity fragmentActivity)
            return map;
        try
        {
            foreach (var fragment in fragmentActivity.SupportFragmentManager.Fragments)
            {
                if (fragment is AndroidX.Fragment.App.DialogFragment { Dialog.Window: { } dialogWindow }
                    && dialogWindow.DecorView is { } dialogDecor)
                    map.TryAdd(dialogDecor, dialogWindow);
            }
        }
        catch
        {
            // fragment manager may be torn down mid-navigation — Draw fallback covers it
        }
        return map;
    }

    /// <summary>PixelCopy of the window's frame at (x, y); falls back to software Draw.</summary>
    static void CaptureWindow(Android.Views.Window? window, AView root, Android.Graphics.Canvas canvas, int x, int y)
    {
        if (window != null && root.Width > 0 && root.Height > 0 && TryPixelCopy(window, root, canvas, x, y))
            return;

        canvas.Save();
        canvas.Translate(x, y);
        root.Draw(canvas);
        canvas.Restore();
    }

    static bool TryPixelCopy(Android.Views.Window window, AView root, Android.Graphics.Canvas canvas, int x, int y)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return false;
        try
        {
            using var windowBitmap = Android.Graphics.Bitmap.CreateBitmap(
                root.Width, root.Height, Android.Graphics.Bitmap.Config.Argb8888!);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PixelCopy.Request(window, windowBitmap, new PixelCopyListener(completion), PixelCopyHandler());
            // The listener fires on a dedicated handler thread, so waiting here is safe
            // even though captures run on the main thread.
            if (!completion.Task.Wait(1000) || !completion.Task.Result)
                return false;
            canvas.DrawBitmap(windowBitmap, x, y, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static Android.OS.Handler? _pixelCopyHandler;

    static Android.OS.Handler PixelCopyHandler()
    {
        if (_pixelCopyHandler == null)
        {
            var thread = new Android.OS.HandlerThread("maui-inspector-pixelcopy");
            thread.Start();
            _pixelCopyHandler = new Android.OS.Handler(thread.Looper!);
        }
        return _pixelCopyHandler;
    }

    sealed class PixelCopyListener(TaskCompletionSource<bool> completion)
        : Java.Lang.Object, PixelCopy.IOnPixelCopyFinishedListener
    {
        public void OnPixelCopyFinished(int copyResult) =>
            completion.TrySetResult(copyResult == (int)PixelCopyResult.Success);
    }

    /// <summary>Dispatches a real down+up touch pair to the topmost window's decor.</summary>
    private partial bool InjectTapPlatform(Point windowDp)
    {
        if (HostDecor() is not { } decor)
            return false;
        try
        {
            var density = Density;
            var x = (float)(windowDp.X * density);
            var y = (float)(windowDp.Y * density);
            var now = Android.OS.SystemClock.UptimeMillis();
            using var down = MotionEvent.Obtain(now, now, MotionEventActions.Down, x, y, 0);
            using var up = MotionEvent.Obtain(now, now + 50, MotionEventActions.Up, x, y, 0);
            decor.DispatchTouchEvent(down);
            decor.DispatchTouchEvent(up);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private partial double GetBottomInsetPlatform()
    {
        var decor = _activity?.Window?.DecorView;
        if (decor == null)
            return 0;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var insets = decor.RootWindowInsets?.GetInsets(WindowInsets.Type.SystemBars());
            return (insets?.Bottom ?? 0) / Density;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
#pragma warning disable CA1422 // deprecated on API 30+, guarded above
            return (decor.RootWindowInsets?.SystemWindowInsetBottom ?? 0) / Density;
#pragma warning restore CA1422
        }

        return 0;
    }
}
