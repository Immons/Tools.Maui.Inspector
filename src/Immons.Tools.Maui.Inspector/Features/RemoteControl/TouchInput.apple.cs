using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// iOS touch synthesis — the technique KIF and similar UI-test drivers use: a UITouch is built
/// through UIKit's private setters, wrapped in the application's touches event together with an
/// IOHIDEvent (required since iOS 9) and handed to <c>UIApplication.sendEvent:</c>. From there it
/// travels the normal path: window hit-test, gesture recognizers, UIControl tracking, scroll views.
/// Uses private API — a development tool only; switch it off with
/// <see cref="MauiInspectorOptions.NativeTouchInjection"/> and never ship it to the App Store.
/// </summary>
internal static partial class TouchInput
{
    static bool? _available;

    private static partial bool PlatformAvailable() => _available ??= UIKitTouch.Probe();

    private static partial ITouchSession? CreatePlatform() => new UIKitTouch();
}

internal sealed class UIKitTouch : ITouchSession
{
    UITouch? _touch;
    UIWindow? _window;

    /// <summary>Checks that every private selector the synthesis needs still exists.</summary>
    public static bool Probe()
    {
        try
        {
            using var touch = new UITouch();
            var app = UIApplication.SharedApplication;
            return touch.RespondsToSelector(new Selector("setWindow:"))
                && touch.RespondsToSelector(new Selector("setView:"))
                && touch.RespondsToSelector(new Selector("setPhase:"))
                && touch.RespondsToSelector(new Selector("_setLocationInWindow:resetPrevious:"))
                && touch.RespondsToSelector(new Selector("setTimestamp:"))
                && app.RespondsToSelector(new Selector("_touchesEvent"));
        }
        catch
        {
            return false;
        }
    }

    public bool Down(Point windowPoint)
    {
        try
        {
            var point = new CGPoint(windowPoint.X, windowPoint.Y);
            if (TargetWindow(point) is not { } window || window.HitTest(window.ConvertPointFromWindow(point, null), null) is not { } view)
                return false;

            _window = window;
            var local = window.ConvertPointFromWindow(point, null);
            var touch = new UITouch();
            var h = touch.Handle;

            // setWindow: wipes other fields — it must come first.
            Objc.SendIntPtr(h, "setWindow:", window.Handle);
            Objc.SendNInt(h, "setTapCount:", 1);
            Objc.SendPointBool(h, "_setLocationInWindow:resetPrevious:", local, true);
            Objc.SendIntPtr(h, "setView:", view.Handle);
            Objc.SendNInt(h, "setPhase:", (nint)(long)UITouchPhase.Began);
            if (touch.RespondsToSelector(new Selector("_setIsFirstTouchForView:")))
                Objc.SendBool(h, "_setIsFirstTouchForView:", true);
            else
                Objc.SetFirstTouchFlag(h);
            if (touch.RespondsToSelector(new Selector("setIsTap:")))
                Objc.SendBool(h, "setIsTap:", true);
            Objc.SendDouble(h, "setTimestamp:", NSProcessInfo.ProcessInfo.SystemUptime);
            if (touch.RespondsToSelector(new Selector("setGestureView:")))
                Objc.SendIntPtr(h, "setGestureView:", view.Handle);
            if (touch.RespondsToSelector(new Selector("_setHidEvent:")))
            {
                var hid = HidEvents.Create(local, UITouchPhase.Began);
                if (hid != IntPtr.Zero)
                {
                    Objc.SendIntPtr(h, "_setHidEvent:", hid);
                    HidEvents.Release(hid);
                }
            }

            _touch = touch;
            Send(UITouchPhase.Began, local);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[MauiInspector] touch injection failed: " + ex.Message);
            _touch = null;
            return false;
        }
    }

    public void Move(Point windowPoint) => Update(windowPoint, UITouchPhase.Moved);

    public void Up(Point windowPoint)
    {
        Update(windowPoint, UITouchPhase.Ended);
        _touch?.Dispose();
        _touch = null;
        _window = null;
    }

    void Update(Point windowPoint, UITouchPhase phase)
    {
        if (_touch == null || _window == null)
            return;
        try
        {
            var local = _window.ConvertPointFromWindow(new CGPoint(windowPoint.X, windowPoint.Y), null);
            var h = _touch.Handle;
            Objc.SendDouble(h, "setTimestamp:", NSProcessInfo.ProcessInfo.SystemUptime);
            Objc.SendPointBool(h, "_setLocationInWindow:resetPrevious:", local, false);
            Objc.SendNInt(h, "setPhase:", (nint)(long)phase);
            Send(phase, local);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[MauiInspector] touch injection failed: " + ex.Message);
        }
    }

    /// <summary>The application's shared touches event, refilled with our touch for this phase.</summary>
    void Send(UITouchPhase phase, CGPoint location)
    {
        var app = UIApplication.SharedApplication;
        var ev = Objc.SendReturnIntPtr(app.Handle, "_touchesEvent");
        if (ev == IntPtr.Zero || _touch == null)
            return;

        Objc.Send(ev, "_clearTouches");
        var hid = HidEvents.Create(location, phase);
        if (hid != IntPtr.Zero)
        {
            Objc.SendIntPtr(ev, "_setHIDEvent:", hid);
            HidEvents.Release(hid);
        }
        Objc.SendIntPtrBool(ev, "_addTouch:forDelayedDelivery:", _touch.Handle, false);
        Objc.SendIntPtr(app.Handle, "sendEvent:", ev);
    }

    /// <summary>
    /// Topmost visible window that accepts a touch at the point — popups (Mopups) and alerts live
    /// in windows of their own. Keyboard windows are skipped: text goes in through /api/text.
    /// </summary>
    internal static UIWindow? TargetWindow(CGPoint screenPoint)
    {
        foreach (var window in OrderedWindows())
        {
            var local = window.ConvertPointFromWindow(screenPoint, null);
            if (window.HitTest(local, null) != null)
                return window;
        }
        return null;
    }

    /// <summary>Visible app windows, topmost first.</summary>
    internal static IEnumerable<UIWindow> OrderedWindows()
    {
        var windows = new List<UIWindow>();
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene windowScene)
                windows.AddRange(windowScene.Windows);
        }

        return windows
            .Select((window, order) => (window, order))
            .Where(t => !t.window.Hidden && t.window.Alpha > 0.01 && !IsKeyboardWindow(t.window))
            .OrderByDescending(t => (double)t.window.WindowLevel)
            .ThenByDescending(t => t.order)
            .Select(t => t.window);
    }

    static bool IsKeyboardWindow(UIWindow window)
    {
        var name = window.Class.Name ?? "";
        return name.Contains("Keyboard", StringComparison.Ordinal) || name.Contains("TextEffects", StringComparison.Ordinal);
    }
}

/// <summary>Raw Objective-C messaging for the private UITouch / UIEvent setters.</summary>
internal static class Objc
{
    const string LibObjc = "/usr/lib/libobjc.dylib";

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern IntPtr MsgSendReturnIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSendNInt(IntPtr receiver, IntPtr selector, nint arg);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSendByte(IntPtr receiver, IntPtr selector, byte arg);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSendDouble(IntPtr receiver, IntPtr selector, double arg);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSendPointByte(IntPtr receiver, IntPtr selector, CGPoint point, byte flag);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    static extern void MsgSendIntPtrByte(IntPtr receiver, IntPtr selector, IntPtr arg, byte flag);

    [DllImport(LibObjc)]
    static extern IntPtr object_getClass(IntPtr obj);

    [DllImport(LibObjc)]
    static extern IntPtr class_getInstanceVariable(IntPtr cls, string name);

    [DllImport(LibObjc)]
    static extern nint ivar_getOffset(IntPtr ivar);

    public static void Send(IntPtr receiver, string selector) => MsgSend(receiver, Selector.GetHandle(selector));

    public static IntPtr SendReturnIntPtr(IntPtr receiver, string selector) =>
        MsgSendReturnIntPtr(receiver, Selector.GetHandle(selector));

    public static void SendIntPtr(IntPtr receiver, string selector, IntPtr arg) =>
        MsgSendIntPtr(receiver, Selector.GetHandle(selector), arg);

    public static void SendNInt(IntPtr receiver, string selector, nint arg) =>
        MsgSendNInt(receiver, Selector.GetHandle(selector), arg);

    public static void SendBool(IntPtr receiver, string selector, bool arg) =>
        MsgSendByte(receiver, Selector.GetHandle(selector), arg ? (byte)1 : (byte)0);

    public static void SendDouble(IntPtr receiver, string selector, double arg) =>
        MsgSendDouble(receiver, Selector.GetHandle(selector), arg);

    public static void SendPointBool(IntPtr receiver, string selector, CGPoint point, bool flag) =>
        MsgSendPointByte(receiver, Selector.GetHandle(selector), point, flag ? (byte)1 : (byte)0);

    public static void SendIntPtrBool(IntPtr receiver, string selector, IntPtr arg, bool flag) =>
        MsgSendIntPtrByte(receiver, Selector.GetHandle(selector), arg, flag ? (byte)1 : (byte)0);

    /// <summary>
    /// iOS 9+ dropped <c>_setIsFirstTouchForView:</c>; the flag is the lowest bit of the
    /// <c>_touchFlags</c> bitfield, which UIKit reads to start a fresh gesture on the view.
    /// </summary>
    public static void SetFirstTouchFlag(IntPtr touch)
    {
        var ivar = class_getInstanceVariable(object_getClass(touch), "_touchFlags");
        if (ivar == IntPtr.Zero)
            return;
        var offset = ivar_getOffset(ivar);
        var flags = Marshal.ReadByte(touch, (int)offset);
        Marshal.WriteByte(touch, (int)offset, (byte)(flags | 0x01));
    }
}

/// <summary>
/// The IOHIDEvent UIKit expects behind every touches event (a "hand" digitizer event with one
/// finger child), built with IOKit's IOHIDEvent functions.
/// </summary>
internal static class HidEvents
{
    const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    const uint TransducerTypeHand = 3;
    const uint EventRange = 1 << 0;
    const uint EventTouch = 1 << 1;
    const uint EventPosition = 1 << 2;
    const uint FieldDigitizerIsDisplayIntegrated = (11u << 16) | 25u; // kIOHIDEventTypeDigitizer, field 25
    const uint OptionsDisplayIntegrated = 0xF0000000;

    [DllImport("/usr/lib/system/libsystem_kernel.dylib")]
    static extern ulong mach_absolute_time();

    [DllImport(IOKit)]
    static extern IntPtr IOHIDEventCreateDigitizerEvent(IntPtr allocator, ulong timeStamp, uint type,
        uint index, uint identity, uint eventMask, uint buttonMask,
        double x, double y, double z, double tipPressure, double barrelPressure,
        byte range, byte touch, uint options);

    [DllImport(IOKit)]
    static extern IntPtr IOHIDEventCreateDigitizerFingerEventWithQuality(IntPtr allocator, ulong timeStamp,
        uint index, uint identity, uint eventMask,
        double x, double y, double z, double tipPressure, double twist,
        double minorRadius, double majorRadius, double quality, double density, double irregularity,
        byte range, byte touch, uint options);

    [DllImport(IOKit)]
    static extern void IOHIDEventAppendEvent(IntPtr parent, IntPtr child, uint options);

    [DllImport(IOKit)]
    static extern void IOHIDEventSetIntegerValueWithOptions(IntPtr hidEvent, uint field, nint value, uint options);

    [DllImport(CoreFoundation)]
    static extern void CFRelease(IntPtr cf);

    static bool _broken;

    /// <summary>A retained IOHIDEvent (release with <see cref="Release"/>), or zero when IOKit refused.</summary>
    public static IntPtr Create(CGPoint location, UITouchPhase phase)
    {
        if (_broken)
            return IntPtr.Zero;
        try
        {
            var time = mach_absolute_time();
            var hand = IOHIDEventCreateDigitizerEvent(IntPtr.Zero, time, TransducerTypeHand, 0, 0, EventTouch, 0,
                0, 0, 0, 0, 0, 0, 1, 0);
            if (hand == IntPtr.Zero)
                return IntPtr.Zero;
            IOHIDEventSetIntegerValueWithOptions(hand, FieldDigitizerIsDisplayIntegrated, 1, OptionsDisplayIntegrated);

            var mask = phase == UITouchPhase.Moved ? EventPosition : EventRange | EventTouch;
            var touching = phase is UITouchPhase.Ended or UITouchPhase.Cancelled ? (byte)0 : (byte)1;
            var finger = IOHIDEventCreateDigitizerFingerEventWithQuality(IntPtr.Zero, time, 1, 2, mask,
                location.X, location.Y, 0, 0, 0, 5, 5, 1, 1, 1, touching, touching, 0);
            if (finger != IntPtr.Zero)
            {
                IOHIDEventSetIntegerValueWithOptions(finger, FieldDigitizerIsDisplayIntegrated, 1, OptionsDisplayIntegrated);
                IOHIDEventAppendEvent(hand, finger, 0);
                CFRelease(finger);
            }
            return hand;
        }
        catch (Exception ex)
        {
            // Missing IOKit symbol on some OS version: touches still go out, just without HID data.
            _broken = true;
            Console.WriteLine("[MauiInspector] IOHIDEvent unavailable: " + ex.Message);
            return IntPtr.Zero;
        }
    }

    public static void Release(IntPtr hidEvent)
    {
        if (hidEvent != IntPtr.Zero)
            CFRelease(hidEvent);
    }
}
