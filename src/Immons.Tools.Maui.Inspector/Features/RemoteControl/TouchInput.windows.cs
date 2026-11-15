namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

// WinUI has no in-process touch injection without a capability prompt — semantic taps apply.
internal static partial class TouchInput
{
    private static partial bool PlatformAvailable() => false;

    private static partial ITouchSession? CreatePlatform() => null;
}
