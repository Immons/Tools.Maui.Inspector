namespace Immons.Tools.Maui.Inspector.Features.Memory.Tracking;

/// <summary>What role a tracked object played when the tracker first saw it.</summary>
internal enum TrackedKind
{
    Element,
    BindingContext,
    Handler,
    PlatformView,
}
