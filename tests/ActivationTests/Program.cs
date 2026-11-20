using Immons.Tools.Maui.Inspector;
using Immons.Tools.Maui.Inspector.Features.Activation;

var failures = 0;

// ── Gesture on/off ────────────────────────────────────────────────────────────────
Check("None installs no gesture", !ActivationGate.GestureEnabled(InspectorActivation.None));
Check("default(enum) is None and installs no gesture", default(InspectorActivation) == InspectorActivation.None
                                                       && !ActivationGate.GestureEnabled(default));
Check("LongPress installs the gesture", ActivationGate.GestureEnabled(InspectorActivation.LongPress));
Check("unknown value installs no gesture", !ActivationGate.GestureEnabled((InspectorActivation)42));

// ── Hold time ─────────────────────────────────────────────────────────────────────
Check("30 s is kept", ActivationGate.EffectiveDuration(TimeSpan.FromSeconds(30)) == TimeSpan.FromSeconds(30));
Check("2 s is kept", ActivationGate.EffectiveDuration(TimeSpan.FromSeconds(2)) == TimeSpan.FromSeconds(2));
Check("short but positive is kept", ActivationGate.EffectiveDuration(TimeSpan.FromMilliseconds(50)) == TimeSpan.FromMilliseconds(50));
Check("zero falls back to 900 ms", ActivationGate.EffectiveDuration(TimeSpan.Zero) == TimeSpan.FromMilliseconds(900));
Check("negative falls back to 900 ms", ActivationGate.EffectiveDuration(TimeSpan.FromSeconds(-1)) == TimeSpan.FromMilliseconds(900));

// ── Memory watch mode ─────────────────────────────────────────────────────────────
Check("Watch mode is on by default", new MemoryOptions().WatchNavigation);
Check("Watch mode can be switched off", !new MemoryOptions { WatchNavigation = false }.WatchNavigation);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}
