using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Immons.Tools.Maui.Inspector.Features.Cookbook;
using Immons.Tools.Maui.Inspector.Features.Memory.Metrics;

var failures = 0;
Console.WriteLine($"MAUI {typeof(VisualStateManager).Assembly.GetName().Version} on .NET {Environment.Version}");

// ── Visual states: names on MAUI 10 and 11 ─────────────────────────────────────────
Check("no sample, no names", VisualStates.NamesOf(null).Count == 0);
Check("no groups, no names", VisualStates.NamesOf(new Button()).Count == 0);

var own = new Button();
VisualStateManager.SetVisualStateGroups(own, Groups(("CommonStates", ["Normal", "Pressed", "Disabled"]), ("FocusStates", ["Focused"])));
Check("own groups: every named state, in order", VisualStates.NamesOf(own).SequenceEqual(["Normal", "Pressed", "Disabled", "Focused"]));

var styled = new Button
{
    Style = new Style(typeof(Button))
    {
        Setters = { new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = Groups(("CommonStates", ["Normal", "PointerOver"])) } },
    },
};
Check("groups from a style", VisualStates.NamesOf(styled).SequenceEqual(["Normal", "PointerOver"]));

// The binary break: VisualStateManager.GetVisualStateGroups returns IList<VisualStateGroup> in MAUI 10
// and VisualStateGroupList in MAUI 11 — a call compiled against one is a missing method on the other.
var calls = CalledMethods(typeof(VisualStates)).ToList();
Check("does not call VisualStateManager.GetVisualStateGroups", calls.Count > 0
    && !calls.Any(m => m.DeclaringType == typeof(VisualStateManager) && m.Name == nameof(VisualStateManager.GetVisualStateGroups)));

// ── Slow reading (Android getMemoryInfo): never on the main thread ────────────────
{
    var reads = 0;
    var probe = new ThrottledProbe<Box>(TimeSpan.FromSeconds(5), () => new Box(++reads), _ => throw new InvalidOperationException("must not queue"));
    Check("off main: read in place, fresh value", probe.Refresh(onMainThread: false, nowMs: 1_000)?.Value == 1);
    Check("within the interval: no second read", probe.Refresh(false, 5_999)?.Value == 1 && reads == 1);
    Check("after the interval: read again", probe.Refresh(false, 6_000)?.Value == 2 && reads == 2);
}
{
    var reads = 0;
    var queued = new List<Action>();
    var probe = new ThrottledProbe<Box>(TimeSpan.FromSeconds(5), () => new Box(++reads), queued.Add);
    Check("main thread, first call: queued, nothing read inline", probe.Refresh(onMainThread: true, nowMs: 0) == null && reads == 0 && queued.Count == 1);
    Check("main thread, read in flight: not queued twice", probe.Refresh(true, 10_000) == null && queued.Count == 1);
    queued[0]();
    Check("queued read lands in Latest", probe.Latest?.Value == 1 && reads == 1);
    Check("main thread, due again: previous value returned, new read queued", probe.Refresh(true, 10_000)?.Value == 1 && queued.Count == 2 && reads == 1);
}
{
    var fail = true;
    var probe = new ThrottledProbe<Box>(TimeSpan.FromSeconds(5), () => fail ? throw new InvalidOperationException("kernel said no") : new Box(7), _ => { });
    probe.Refresh(false, 0);
    Check("failed read: no value", probe.Latest == null);
    fail = false;
    Check("failed read does not leave it busy", probe.Refresh(false, 5_000)?.Value == 7);
}
{
    var reads = 0;
    var queueFails = true;
    var probe = new ThrottledProbe<Box>(TimeSpan.Zero, () => new Box(++reads), work => { if (queueFails) throw new NotSupportedException(); work(); });
    probe.Refresh(true, 0);
    queueFails = false;
    Check("queue that throws does not leave it busy", probe.Refresh(true, 1)?.Value == 1);
}
{
    // The real thing: a slow read handed to the thread pool from the "main" thread.
    var caller = Environment.CurrentManagedThreadId;
    var readOn = 0;
    using var done = new ManualResetEventSlim();
    var probe = new ThrottledProbe<Box>(TimeSpan.FromSeconds(5), () =>
    {
        readOn = Environment.CurrentManagedThreadId;
        Thread.Sleep(400);
        done.Set();
        return new Box(1);
    }, work => ThreadPool.UnsafeQueueUserWorkItem(static w => w(), work, preferLocal: false));
    var watch = Stopwatch.StartNew();
    probe.Refresh(onMainThread: true, Environment.TickCount64);
    var blocked = watch.ElapsedMilliseconds;
    Check($"main thread is not blocked by a 400 ms read ({blocked} ms)", blocked < 100);
    Check("read ran on another thread", done.Wait(TimeSpan.FromSeconds(5)) && readOn != caller && readOn != 0);
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
        failures++;
}

static VisualStateGroupList Groups(params (string Name, string[] States)[] groups)
{
    var list = new VisualStateGroupList();
    foreach (var (name, states) in groups)
    {
        var group = new VisualStateGroup { Name = name };
        foreach (var state in states)
            group.States.Add(new VisualState { Name = state });
        list.Add(group);
    }
    return list;
}

// Every method a type's own methods call — call/callvirt/newobj operands resolved from the IL.
static IEnumerable<MethodBase> CalledMethods(Type type)
{
    var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var i = 0; i < il.Length;)
        {
            var value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            if (!opcodes.TryGetValue(value, out var op))
                yield break;
            var size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (op.OperandType == OperandType.InlineMethod)
            {
                MethodBase? called = null;
                try
                {
                    called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i));
                }
                catch (Exception)
                {
                    // generic context or unresolvable — not a VisualStateManager call either way
                }
                if (called != null)
                    yield return called;
            }
            i += size;
        }
    }
}

sealed record Box(int Value);
