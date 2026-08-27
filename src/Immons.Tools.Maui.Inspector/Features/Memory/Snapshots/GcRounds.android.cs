namespace Immons.Tools.Maui.Inspector.Features.Memory.Snapshots;

internal static partial class GcRounds
{
    static partial void CollectPlatform() => Java.Lang.Runtime.GetRuntime()?.Gc();
}
