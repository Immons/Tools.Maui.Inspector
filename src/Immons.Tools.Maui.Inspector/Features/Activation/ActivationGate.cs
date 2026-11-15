namespace Immons.Tools.Maui.Inspector.Features.Activation;

/// <summary>
/// Single source of truth for the on-device activation gesture: whether it exists at all and how long
/// it has to be held. MAUI-free so the rules are unit-tested (tests/ActivationTests).
/// </summary>
internal static class ActivationGate
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(900);

    /// <summary>Only <see cref="InspectorActivation.LongPress"/> installs a gesture; None and unknown values never do.</summary>
    public static bool GestureEnabled(InspectorActivation activation) => activation == InspectorActivation.LongPress;

    /// <summary>The configured hold time, or the default when it is zero or negative.</summary>
    public static TimeSpan EffectiveDuration(TimeSpan configured) => configured > TimeSpan.Zero ? configured : DefaultDuration;
}
