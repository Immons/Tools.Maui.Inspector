using System.Runtime.CompilerServices;
using Immons.Tools.Maui.Inspector.Inspector;
using Immons.Tools.Maui.Inspector.Web;

namespace Immons.Tools.Maui.Inspector;

/// <summary>
/// Facade for the in-app visual tree inspector. Use <see cref="MauiInspectorBuilderExtensions.UseMauiInspector"/>
/// to enable it, then activate via the configured gesture or the methods below.
/// </summary>
public static class MauiInspector
{
    static readonly ConditionalWeakTable<Window, WindowInspector> Inspectors = new();

    internal static MauiInspectorOptions Options { get; } = new();

    /// <summary>Inspector of the current window (used by the embedded web server).</summary>
    internal static WindowInspector? ActiveInspector => Resolve(null);

    internal static void OnWindowHandlerConnected(Window window)
    {
        if (Options.EnableWebServer)
            RemoteServer.EnsureStarted(Options.WebServerPort);

        if (Options.ShakeToOpen)
            ShakeActivation.EnsureStarted();

        // Replays persisted structural edits (SQLite backend) as pages appear.
        InspectorServices.Current.Replay.Hook();

        if (!Inspectors.TryGetValue(window, out var inspector))
        {
            inspector = new WindowInspector(window, Options);
            Inspectors.Add(window, inspector);
            window.Destroying += (_, _) =>
            {
                if (Inspectors.TryGetValue(window, out var i))
                {
                    i.Hide();
                    i.Detach();
                }
            };
        }

        inspector.OnHandlerChanged();
    }

    /// <summary>
    /// Base URL of the embedded web inspector (e.g. "http://localhost:9295"),
    /// or null when <see cref="MauiInspectorOptions.EnableWebServer"/> is off or it failed to start.
    /// </summary>
    public static string? WebServerUrl => Web.Hosting.RemoteServer.Url;

    /// <summary>Why the embedded web server failed to start; null when it runs or was not enabled.</summary>
    public static string? WebServerStartError => Web.Hosting.RemoteServer.StartError;

    /// <summary>
    /// Mock scenario currently selected in the panel ("" when none). Debug builds can branch on it
    /// to fake things the HTTP layer cannot reach — an MSAL sign-in, a native SDK, a sensor.
    /// </summary>
    /// <summary>
    /// Loads a rule set exported from the panel ({ scenarios, activeScenario, rules }) and returns
    /// how many rules were added. Useful from a test hook when bundling the file is not an option.
    /// </summary>
    public static int ImportRules(string json) =>
        Features.NetworkInspection.RuleSeed.Apply(InspectorServices.Current.NetworkRules, json);

    public static string ActiveScenario => InspectorServices.Current.NetworkRules.ActiveScenario;

    /// <summary>True when the named scenario (or any scenario, with no argument) is active.</summary>
    public static bool IsScenarioActive(string? name = null) =>
        name == null ? ActiveScenario.Length > 0
        : string.Equals(ActiveScenario, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens the inspector overlay on the current window.</summary>
    public static void Show() => Show(null);

    /// <summary>Opens the inspector overlay on the given window (or the current one when null).</summary>
    public static void Show(Window? window)
    {
        if (Resolve(window) is { } inspector)
            inspector.Show(null);
    }

    /// <summary>Closes the inspector overlay.</summary>
    public static void Hide(Window? window = null) => Resolve(window)?.Hide();

    /// <summary>Toggles the inspector overlay.</summary>
    public static void Toggle(Window? window = null)
    {
        if (Resolve(window) is { } inspector)
        {
            if (inspector.IsShown)
                inspector.Hide();
            else
                inspector.Show(null);
        }
    }

    /// <summary>
    /// Opens the design cookbook: a page listing the app's styles, controls, colors, fonts,
    /// images and templates as live samples (also reachable from the web panel and the overlay's ⋯ row).
    /// </summary>
    public static Task ShowCookbook() => OnMainThread(() => InspectorServices.Current.Cookbook.OpenAsync(null));

    /// <summary>Closes the design cookbook page when it is open.</summary>
    public static Task HideCookbook() => OnMainThread(InspectorServices.Current.Cookbook.CloseAsync);

    static Task OnMainThread(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher == null || !dispatcher.IsDispatchRequired ? action() : dispatcher.DispatchAsync(action);
    }

    /// <summary>Opens the inspector with the given element selected.</summary>
    public static void Inspect(VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (Resolve(element.Window) is { } inspector)
        {
            inspector.Show(null);
            inspector.SelectElement(element);
        }
    }

    static WindowInspector? Resolve(Window? window)
    {
        window ??= Application.Current?.Windows.FirstOrDefault(w => w.Handler != null)
                   ?? Application.Current?.Windows.FirstOrDefault();
        return window != null && Inspectors.TryGetValue(window, out var inspector) ? inspector : null;
    }
}
