---
name: memory-leak-hunting
description: Finding and fixing managed memory leaks in .NET MAUI apps (iOS/Android/WinUI) using heap dumps and GC-root chains. Use when views, view models or controls survive navigation, when a leak inspector reports detached-but-alive objects, or when memory grows across repeated navigation cycles.
---

# Hunting memory leaks in .NET MAUI

## The one rule that matters

**A leak is a count that grows. Everything else is noise.**

Never conclude from a single snapshot. Take two measurements separated by N identical
navigation cycles, with everything already materialised before the first one:

```
cycle × 4  →  snapshot A  →  cycle × 4  →  snapshot B
```

- `B == A` → not a leak, however ugly the number. It is a cache, a live screen, or the
  last instance being pinned.
- `B > A` → real leak, and `(B − A) / cycles` is its cost per navigation.

Objects marked *"just detached"*, *"survived 1 snapshot · 0s"* are usually still on their
way out. Only things surviving several snapshots are candidates.

## Before measuring: verify the scenario actually ran

More measurements are wrecked by a broken script than by a wrong hypothesis. Every cycle
step needs a state assertion — tap, then confirm you landed where you expected.

Failures seen in practice:
- The tapped label existed on **both** screens, so the "cycle" never navigated, and the
  identical counts looked like proof of no leak.
- The test entity had no data (opened a visit with no activations while hunting an
  `Activation` leak).
- The date rolled over at midnight and the list became empty.
- Measured a different process — another app, or a port forwarded elsewhere
  (`adb forward` can redirect `localhost:<port>` to a device).

Confirm the process: `lsof -nP -iTCP -sTCP:LISTEN | grep <AppName>`. **Two rows on one port
means two apps are answering it** (see the adb note below) — the panel says *two apps on this
port*, and over HTTP the giveaway is `/api/ping` returning a different `app`, `pid` or
`instance` between two calls. `/api/memory` also reports `app` and `pid`; check they are the
ones you think they are before believing any number.

## Getting the data

### Leak inspector (fast, first stop)

For MAUI there is a ready-made in-app inspector — the **`Immons.Tools.Maui.Inspector`**
NuGet package (pair it with `Immons.Tools.Maui.Inspector.Diagnostics` for heap-dump
support). It hosts an HTTP server inside the running app, so it works on a real device or
simulator with no debugger attached:

```xml
<PackageReference Include="Immons.Tools.Maui.Inspector" Version="0.9.18" />
<PackageReference Include="Immons.Tools.Maui.Inspector.Diagnostics" Version="0.9.18" />
```

On Android the port lives in the emulator's own loopback, so the host needs a forward — let
`maui-inspector-sync` do it (it finds every device and maps each app onto a free host port,
printing the URL). **Never forward 1:1 by hand.** An iOS simulator app runs as a host process
and binds the Mac's port of the same number; `adb forward tcp:9295 tcp:9295` then leaves two
listeners on one port and requests alternate between two different apps — you get one app's
header over another's numbers. If you must do it by hand, shift the host port:
`adb forward tcp:19295 tcp:9295`.

Open `http://localhost:9295/#memory` in a browser, or drive it over HTTP:

```bash
curl -s http://localhost:9295/api/memory                                   # counters, tracking, ledger
curl -s -X POST -H 'Content-Type: application/json' -d '{}'      http://localhost:9295/api/memory/gc                                    # force a GC
curl -s -X POST -H 'Content-Type: application/json' -d '{}'      http://localhost:9295/api/memory/snapshot                              # snapshot + suspects
```

`POST` endpoints reject a request with no body — send `-d '{}'` or you get `411 Length
Required`. A snapshot already runs several full collections with finaliser waits before it
counts anything (`options.Memory.CollectionsPerSnapshot`, default 5), so an extra
`/api/memory/gc` first buys nothing — `/gc` is for reading the counters without a snapshot.

What the snapshot gives you per suspect: `type`, `name`, `kind`, `survived` (how many
snapshots it has outlived), `ageMs`, `owner`, `hints`, `parents`, `holders` — and `totals` with
`tracked` / `attached` / `detached` / `collected`. **Read these before dumping the heap**; they
frequently name the culprit outright.

`hints` — what the tracker can tell from the object's own state:

- `handler still connected — DisconnectHandler never ran` — nothing disconnected the view.
- `platform view still alive` — the native peer is still there.
- `no attached element is bound to it` — a binding context whose view is gone.
- `inside <Type>` — a child dragged along by an ancestor; fix the ancestor, not this.

`holders` — what the in-process scan actually found referencing it, and the closest thing to a
root chain without a dump:

- `static event LeakSource.Tick → OnTick` — the exact subscription to cut.
- `static field Cache.Current`, `collection Registry.Items`, `singleton SyncService` as owner.

A suspect that is *"just detached"* or has `survived: 1` with a tiny `ageMs` is still on its
way out, not yet evidence. Note the snapshot already exonerates objects a live screen can still
reach (a filter built on one page and read by the next), so what is left is genuinely unowned.

The Navigation ledger tab shows pages pushed/popped and whether each was collected —
the quickest way to see that a screen survives its own dismissal. It only sees `Page`s coming
and going from a window; overlays and popups hosted inside a page have to report themselves
with `Immons.Tools.Maui.Inspector.Navigation.ReportPushed/ReportPopped`.

Tracking can be switched off at runtime (**⏻ Tracking** in the panel, or
`POST /api/memory/settings {"tracking":false}`, or `options.Memory.TrackInstances` at startup).
Off means the registry is emptied and nothing is recorded — so `tracking.enabled: false` in
`/api/memory` explains an empty suspects list, and turning it back on re-reads the visual tree
but starts the counting from scratch. Check that flag before concluding "no leaks".

`holders` carries what the in-process scan found (static fields and events in your own
assemblies, plus the Application/Window/Shell events) — often enough to name the culprit with
no dump at all. Empty `holders` on a real leak points at a native anchor, which no managed
scan can see.

**Once a heap dump exists, the snapshot itself carries the chains**: every suspect also gets
`chains` (shortest paths to a GC root), `rootKind` (`static` / `interop` / `handle` / …),
`retained` and the `dumpJob` they came from. So a script is two calls — `heapdump`, then
`snapshot` — and never has to read the panel. The navigation ledger only labels entries
correctly from 0.9.17 onwards.

### Root chains from the inspector (this is the authoritative source)

`POST /api/memory/heapdump` orders a dump and **blocks until the report is back** — one call
instead of the request/poll dance, and you rarely touch `dotnet-gcdump` yourself:

```bash
curl -s -X POST -H 'Content-Type: application/json' -d '{}' \
     http://localhost:9295/api/memory/heapdump
```

**The app does not collect the dump itself.** It only publishes the job; `maui-inspector-sync`
running on your desktop picks it up and drives `dotnet-gcdump` (through `dotnet-dsrouter` on
Android and iOS). Two preconditions, and without them the job sits pending and fails on the
timeout:

- `maui-inspector-sync` running (it also installs `dotnet-gcdump`/`dotnet-dsrouter` on first use);
- the `Immons.Tools.Maui.Inspector.Diagnostics` package referenced, which gives Debug builds
  the diagnostic port. Check `/api/memory` → `"diagnostics"` and `"diagnosticsAvailable"`.

It takes the current suspects automatically; pass `{"types":["Full.Type.Name"]}` (an **array**
named `types`) to focus specific ones, and `{"timeoutMs":900000}` to wait longer than the
default 7 minutes — collecting streams every object out of the app, roughly a minute per
million. The reply is `{ok, waiting, job, hint}`; when `waiting` is true the dump outlived the
wait — poll `GET /api/memory/dumps` and then `GET /api/memory/dump/report?id=<n>`.

The `job.report` gives you:

- **`roots[]`** — per type: `matched`, `retained` (bytes!), and `paths[]` — **the full chain
  to the GC root, already resolved**. Read each path left-to-right: object → holder →
  holder → `[strong Handles]` → `[other roots]`.
- **`types[]`** — histogram with `count`, `bytes` and an `app: true` flag, so you can
  separate your code from framework noise.
- **`largest[]`** — biggest retainers with their full path.
- `job.file` — the `.gcdump` on disk if you want to dig further.

`retained` is the number that tells you whether a leak is worth fixing; a count alone does
not.

### Manual gcdump (fallback only)

Only when the app has no inspector, or you need something the report does not expose.

```bash
# the inspector already tells you the port: /api/memory → "diagnostics": "127.0.0.1:9084,..."
dotnet-dsrouter server-client -tcpc 127.0.0.1:<DIAG_PORT> &   # note the pid it prints
dotnet-gcdump collect -p <ROUTER_PID> -o dump.gcdump -t 180
```

To walk chains yourself, reference **`dotnet-gcdump.dll`** from
`~/.dotnet/tools/.store/dotnet-gcdump/<ver>/...` — `Graphs.MemoryGraph` and `Graphs.RefGraph`
are public *there* and absent from the TraceEvent NuGet package. BFS **backwards** over
`RefGraph` until you hit a node whose name starts with `[`, then group identical paths —
300 objects usually share one root. A root histogram over a sample of app objects ranks
what to fix first; on one app a single static event held **77.8%** of all live objects.

Writing this yourself costs an hour and gives you less than the report above (no `retained`
bytes). Reach for it deliberately, not by default.

## Reading a chain

Read top-down: each row is held by the one below.

```
NoteItemSection → ExtendedObservableCollection → VisitNotesSidebarPO
  → PropertyChangedEventHandler → Visit (entity)
  → Dictionary<object,CGSize> → GridViewLayout → [strong Handles]
```

- **The first app-owned type below the leaked object is where you fix it.** Framework
  nodes above it are plumbing. *Your* code, not merely non-framework: a third-party NuGet
  (`CommunityToolkit.Maui.Behaviors.TouchBehavior` and friends) is somebody else's plumbing
  too, and pointing at it wastes a round. The report flags the three apart with `app` and
  `package`; keep walking down until a type from your own assemblies appears.
- `[static var X]` — a static field. Deterministic, always worth fixing.
- `[strong Handles]` — interop anchor (ObjC/Java peer). Managed code cannot release it;
  the *native* object must go away. Usually means a handler was never disconnected.
- `[Dependent Handles]` — conditional weak table; rarely the real cause.
- An empty type name in a chain is a compiler-generated closure (`<>c__DisplayClass`) —
  i.e. a lambda captured something.

**Ratio beats magnitude.** 618 closures against 44 owning controls (14:1) is a leak; 44
against 44 is correct.

## The recurring causes

Ordered by how often they actually turn up.

### 1. Long-lived event, short-lived subscriber

Anything static or app-lifetime (`DeviceDisplay.MainDisplayInfoChanged`, `Window.SizeChanged`,
a singleton service, a messenger, an entity that outlives the screen) holds every subscriber
that forgets to unsubscribe.

Fix — a **weak broker**: one real subscription, subscribers held as
`List<WeakReference<T>>`, pruned on every event. A missed `Dispose` then costs nothing:

```csharp
private static class Broker
{
    private static readonly List<WeakReference<Sub>> Subscribers = new();
    static Broker() => Source.Event += OnEvent;

    public static void Add(Sub s) { lock (Subscribers) Subscribers.Add(new(s)); }
    public static void Remove(Sub s) { /* drop dead + matching entries */ }

    private static void OnEvent(object sender, EventArgs e)
    {
        var alive = new List<Sub>();
        lock (Subscribers)
            for (var i = Subscribers.Count - 1; i >= 0; i--)
                if (Subscribers[i].TryGetTarget(out var t)) alive.Add(t);
                else Subscribers.RemoveAt(i);
        foreach (var s in alive) s.Handle(e);
    }
}
```

### 2. `+=` with a lambda in an attach hook

`OnAttachedTo`/`OnLoaded` subscribing with an anonymous lambda can never be unsubscribed,
and re-attach (list-cell recycling!) adds another one every time. Always a named method,
always removed in the matching detach hook.

### 3. `Unloaded` is not reliable — never rely on it alone

Views hosted in popups, or removed via `Children.Remove`, frequently never raise
`Unloaded`. Pair every `Loaded` subscription with an unsubscribe that also runs from
`OnHandlerChanging(args.NewHandler == null)`, or make the subscription weak.

Related trap: when logic moves from a page to its content, it is easy to move `Loaded` and
leave `Unloaded` behind. Audit with `grep -c 'Event +='` vs `-=` per file.

### 4. MAUI does not clean up when a view leaves the tree

Removing a view disconnects nothing. The platform view keeps a native→managed callback and,
through the element's `NameScope`, **one dangling image can pin an entire page graph**.

Fix — an explicit teardown after the page is off the stack:

```csharp
foreach (node in visualTreeDescendants, deepest first)
{
    (node as View)?.GestureRecognizers?.Clear();
    if (node is VisualElement v)
    {
        v.CancelAnimations();      // static animation cache, see 6
        v.Behaviors?.Clear();      // OnDetachingFrom only fires when leaving Behaviors
        v.Handler?.DisconnectHandler();
    }
}
```

Order and platform matter:
- Run it **only after** the subtree is out of the tree — disconnecting a view still being
  laid out aborts the process (SIGABRT on iOS).
- **iOS/MacCatalyst only.** On Android the same teardown desynchronises managed `Children`
  from the native `ViewGroup` → `IndexOutOfBoundsException` on the next open.
- Wrap every node in its own try/catch.

### 5. Views cached outside the visual tree

Tab/step caches (`Dictionary<TKey, View>` in code-behind) are the classic blind spot: the
cached view is **not a descendant**, so a tree walk never reaches it and its handler stays
connected.

Let hosts declare them and have the teardown ask **every node**, not just the root:

```csharp
public interface ICachedViewHost { IEnumerable<View> GetCachedViews(); }
```

Find them with `grep -rn "Dictionary<.*View>" --include="*.xaml.cs"`.

Note such a cache is often *deliberate* (preserving scroll position). Fixed size = a constant
cost, not a leak — confirm with the two-snapshot rule before touching it.

### 6. Unfinished animations

A repeating animation (`repeat: () => true`) lives in the static animation registry and
holds its target forever. Starting one from `Loaded` is a trap: `Loaded` fires
asynchronously and can land *after* teardown, so the animation outlives everything.

Start animations only once the view is confirmed attached, keep the reference, and abort by
name on dispose. Add `CancelAnimations()` to the teardown as a safety net.

### 7. `CollectionView` cell-size cache (`ClearCellSizeCache`)

On iOS, `ItemsViewLayout` caches every measured cell size in
`Dictionary<object, CGSize> _cellSizeCache`, **keyed by the item object itself**, and never
evicts. Replacing `ItemsSource` does not clear it. On a screen that dies this vanishes with
the handler, but on a **long-lived list rebuilt repeatedly** (a tab switching weeks/months,
a filtered product list) it accumulates every item ever displayed for the whole session.

Chain to look for:

```
[strong Handles] → GridViewLayout / ListViewLayout
  → Dictionary<System.Object,CoreGraphics.CGSize> → <your item/PO/entity>
```

MAUI exposes `ClearCellSizeCache()` on `ItemsViewLayout`, but it is **internal**, so it
needs reflection. Confirm it exists in your MAUI version before writing the code:

```bash
strings <app>/Microsoft.Maui.Controls.dll | grep -i cellsize
# CacheCellSize / ClearCellSizeCache / TryGetCachedCellSize / _cellSizeCache
```

Subclass `CollectionView` and clear on both triggers — `ItemsSource` replacement **and**
`NotifyCollectionChangedAction.Reset` (what an observable collection's bulk `ReplaceAll`
raises):

```csharp
protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
{
    base.OnPropertyChanged(propertyName);
    if (propertyName == ItemsSourceProperty.PropertyName)
    {
        RebindItemsCollectionChanged();   // also unsubscribe in OnHandlerChanging(New == null)
        ClearCellSizeCache();
    }
}

private void ClearCellSizeCache()
{
    try
    {
        // PlatformView is a UICollectionViewControllerWrapperView, NOT the UICollectionView —
        // casting it directly silently no-ops and the cache is never cleared.
        var cv = (Handler as IPlatformViewHandler)?.ViewController is UICollectionViewController c
            ? c.CollectionView
            : FindCollectionView(Handler?.PlatformView as UIView);   // recursive Subviews fallback

        if (cv?.CollectionViewLayout is not { } layout) return;

        var clear = Cache.GetOrAdd(layout.GetType(), static t => t.GetMethod(
            "ClearCellSizeCache", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        clear?.Invoke(layout, null);
    }
    catch (Exception) { /* version-specific internals: failing costs memory, not correctness */ }
}
```

Cache the `MethodInfo` **per layout type** (`ListViewLayout` and `GridViewLayout` are
different types), keep it iOS/MacCatalyst-only, and wrap everything in try/catch.

**The trap that makes this fix silently do nothing:** `Handler.PlatformView` is a wrapper
view, not the `UICollectionView`. A direct cast returns null, the method returns early, and
the code looks correct while clearing nothing — verify with a dump, not by reading it.

To roll it out widely, re-base your existing custom list control on the new one instead of
editing every XAML file. When you do rewrite XAML in bulk: match `<CollectionView` with
`[\s>/]` (a newline after the tag name is common, and `[ >/]` misses it), and check whether
the `controls:` prefix is already bound to a *different* namespace in that file, or you get
`duplicate attribute name`. Validate `opens == closes + self-closing` per file before saving.

### 8. DI-held objects with strong back-references

Actions/command-builders/factories living in the container that keep a `DataContext`
pointing at a view model root the entire VM graph for the process lifetime. Make that
reference `WeakReference<T>`.

### 9. A hidden empty `Dispose()`

```csharp
public virtual void Dispose() { }   // hides the base implementation
```

This silently disables cleanup in every subclass. The compiler says so — **CS0108** — so
read build warnings, not just errors. Also check that `Dispose`/`Destroy` has *any* call
site at all; a teardown method nobody invokes is dead code.

## What is NOT a leak

Do not "fix" these:

- **Caches with a bounded size or a TTL** (`MemoryCache`, a 3-entry tab cache). Constant
  count, held by a data layer or by design.
- **Entities outliving the view.** A cached entity is supposed to survive the screen that
  showed it.
- **The currently visible screen.** Dumps taken while the screen is open show its whole
  graph as live — always return to a neutral screen first.
- **Static cached lambdas** (`<>c.<>9`), empty arrays, singletons.
- **"Handler still connected" on a deliberately cached view** — annoying, constant, not
  growing.

## Workflow

0. If the app has no inspector, add `Immons.Tools.Maui.Inspector` first — a suspects list
   with hints saves hours of chain-walking.
1. Reproduce with a scripted cycle that asserts state at every step.
2. Two snapshots, N cycles apart, everything pre-materialised. No growth → stop.
3. Heap dump; root histogram over app objects to rank causes.
4. Full chains for the top type; group them — one root usually explains hundreds.
5. Fix the **first app-owned type** in the chain, matching it to the causes above.
6. Re-measure the same way. Report before/after counts, not impressions.
7. Repeat: fixing the dominant anchor exposes the next one. Expect several rounds.

## Reporting honestly

- Say what you measured and what you assumed. If you could not verify something
  (a platform you cannot build, an interaction you could not script), say so.
- Distinguish "does not grow, one instance pinned" from "grows every cycle" — very
  different priorities.
- If a fix cannot be shown to change the numbers, call it preventive rather than claiming
  a win.
