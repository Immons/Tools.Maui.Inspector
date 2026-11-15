namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

// Plain net10.0: no platform, no touches.
internal static partial class TouchInput
{
    private static partial bool PlatformAvailable() => false;

    private static partial ITouchSession? CreatePlatform() => null;
}
