using Microsoft.Maui.Handlers;

namespace Immons.Tools.Maui.Inspector;

public static class MauiInspectorBuilderExtensions
{
    static bool _mapped;

    /// <summary>
    /// Enables the in-app hierarchy viewer. Typically wrapped in <c>#if DEBUG</c> in the app project.
    /// </summary>
    public static MauiAppBuilder UseMauiInspector(this MauiAppBuilder builder, Action<MauiInspectorOptions>? configure = null)
    {
        // Add-ons (SignalR) attach only to an enabled inspector — without this call they stay inert.
        MauiInspector.IsEnabled = true;

        // Must run before any XAML page is inflated so MAUI records element source locations.
        Features.XamlSync.XamlSource.EnableDiagnostics();

        // The inspector's object graph lives in the app's own container.
        Inspector.InspectorServiceRegistration.AddMauiInspectorServices(builder.Services);

        configure?.Invoke(MauiInspector.Options);

        if (!_mapped)
        {
            _mapped = true;
            WindowHandler.Mapper.AppendToMapping("MauiInspector", (handler, window) =>
            {
                if (window is Window w)
                    MauiInspector.OnWindowHandlerConnected(w);
            });
        }

        return builder;
    }
}
