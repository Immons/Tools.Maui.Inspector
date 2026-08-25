namespace Immons.Tools.Maui.Inspector;

/// <summary>How the inspector overlay gets activated at runtime.</summary>
public enum InspectorActivation
{
    /// <summary>No gesture; call <see cref="MauiInspector.Show()"/> manually.</summary>
    None,

    /// <summary>A long press anywhere in the window opens the inspector on the pressed element.</summary>
    LongPress,
}

public sealed class MauiInspectorOptions
{
    /// <summary>Gesture that activates the inspector. Default: <see cref="InspectorActivation.LongPress"/>.</summary>
    public InspectorActivation Activation { get; set; } = InspectorActivation.LongPress;

    /// <summary>How long the press must be held before the inspector opens. Default: 900 ms.</summary>
    public TimeSpan LongPressDuration { get; set; } = TimeSpan.FromMilliseconds(900);

    /// <summary>
    /// Number of fingers required for the activation long-press (1 or 2).
    /// Use 2 to avoid conflicts with long-press gestures of the app itself. Default: 1.
    /// </summary>
    public int LongPressTouchCount { get; set; } = 1;

    /// <summary>Height of the bottom inspector panel as a fraction of the window height. Default: 0.45.</summary>
    public double PanelHeightFraction { get; set; } = 0.45;

    /// <summary>
    /// Starts an embedded HTTP server exposing the inspector as a web page, so the hierarchy can be
    /// browsed and edited from a desktop browser instead of the on-device overlay. Default: false.
    /// iOS simulator: open http://localhost:port on the Mac. Android emulator: run
    /// <c>adb forward tcp:port tcp:port</c> first. Physical devices: use the device IP.
    /// </summary>
    public bool EnableWebServer { get; set; }

    /// <summary>TCP port of the embedded web server. Default: 9295.</summary>
    /// <summary>
    /// Null (default) = automatic: the server picks the first free port from 9295 upwards,
    /// so several apps/instances coexist and the web client's scan finds them all.
    /// Set a value to force that exact port (no fallback).
    /// </summary>
    public int? WebServerPort { get; set; }

    /// <summary>Also toggle the inspector by shaking the device. Default: false.</summary>
    public bool ShakeToOpen { get; set; }

    /// <summary>
    /// Rule set loaded when the app starts with no rules at all — a file exported from the panel,
    /// added to the app project as <c>MauiAsset</c> (e.g. "inspector-rules.json"). Meant for UI
    /// tests, which wipe app data on every run. The <c>inspectorRules</c> launch argument overrides it.
    /// </summary>
    public string? SeedRulesAsset { get; set; }

    /// <summary>
    /// Largest HTTP body kept for the Network view, in bytes. Default: 4 MB. Bodies are held in
    /// memory (200 most recent calls), so raise it deliberately on memory-constrained devices.
    /// Anything bigger is still logged and mockable — only its body is dropped.
    /// </summary>
    public int MaxCapturedBodyBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>The design cookbook — the gallery of the app's styles, controls, colors, fonts and images.</summary>
    public CookbookOptions Cookbook { get; } = new();
}
