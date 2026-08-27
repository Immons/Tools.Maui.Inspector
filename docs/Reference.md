# Reference

### Supported platforms

| Platform | TFM | Activation |
| --- | --- | --- |
| Android (API 21+) | `net10.0-android` | long-press (1–2 fingers), shake |
| iOS 15+ | `net10.0-ios` | long-press (1–2 fingers), shake |
| Windows (WinUI 3) | `net10.0-windows10.0.19041.0` | `Ctrl+Shift+I` or touch press-and-hold |

The `net10.0` target compiles to no-ops, so referencing the library never breaks other targets.
The Windows target only builds on Windows machines (guarded in the csproj).

### Options

| Option | Default | Description |
| --- | --- | --- |
| `EnableWebServer` | `false` | Embedded web panel for desktop browsers. |
| `WebServerPort` | `null` (auto) | `null` picks a free port from 9295–9309; a value forces that exact port. |
| `Activation` | `LongPress` | `LongPress` or `None` (manual `Show()` only). |
| `LongPressDuration` | 900 ms | Hold time before the overlay opens (iOS/Android). |
| `LongPressTouchCount` | 1 | 1 or 2 fingers. |
| `ShakeToOpen` | `false` | Shake the device to toggle the overlay. |
| `PanelHeightFraction` | 0.45 | On-device panel height as a fraction of the window. |
| `SeedRulesAsset` | `null` | Rule set (a panel export added as `MauiAsset`) imported when the app starts with no rules — see [UI tests](UI-tests.md#ui-tests-maestro-appium). |
| `MaxCapturedBodyBytes` | 4 MB | Largest HTTP body kept for the Network view; bigger ones are still logged and mockable, only the body is dropped. |
| `Cookbook.IncludedControls` | empty | When set, only controls matching these prefixes (namespace, full type name or XAML folder path) are rendered in the Controls section. |
| `Cookbook.ExcludedControls` | empty | The same prefixes, vetoing — controls whose constructor starts hardware, timers or network, or a legacy namespace. |
| `Cookbook.IncludedResources` / `ExcludedResources` | empty | The same idea for colors, styles, templates, images, fonts and scalars: prefixes of a resource key, dictionary file, image/font file or style target type, optionally scoped with `section:`. |
| `Cookbook.BindingContext` | `null` | Factory of the data context every cookbook sample gets — localized strings, theme colors, services the bindings reach for. |
| `Cookbook.LightBackground` / `DarkBackground` / `Background` | `null` | Backdrop behind the samples per theme (or one brush for both); default: the app's implicit page style, else white / `#121212`. |

### Storage backend

By default everything the inspector persists — mock rules, scenarios, breakpoints, applied
expressions — lives in `Preferences`. That is dependency-free and fine for a handful of rules, but
it stores the whole rule set as **one value**, so every change re-serialises all of it. Recording a
real app's traffic gets you there quickly: 190 rules with response bodies is ~1.4 MB rewritten on
every toggle.

`Immons.Tools.Maui.Inspector.Persistency` swaps that for SQLite, where a rule is a row:

```csharp
builder
    .UseMauiInspector(options => { /* … */ })
    .UseMauiInspectorPersistency();          // ← one line, next to UseMauiInspector
```

Rules, scenarios and breakpoints stored by an earlier run are migrated on first start and the old
Preferences copy is removed; pass `migrateFromPreferences: false` to skip that. Applied expressions
are the exception: they are keyed by an opaque hash and `Preferences` cannot be enumerated, so they
are not migrated — they land in SQLite the next time you apply an edit. The database defaults to
`maui-inspector.db3` in the app data folder; pass a path to put it elsewhere.

### How it works

- `UseMauiInspector` appends to `WindowHandler.Mapper`, so every window gets an inspector when its handler connects.
- Android: the activity's `Window.Callback` is wrapped to observe (never consume) touches for long-press detection; overlay layers are added to the `DecorView`. iOS: a non-cancelling `UILongPressGestureRecognizer` on the `UIWindow`; layers are added as window subviews. Windows: a `KeyboardAccelerator` + `Holding` handler on the root content.
- The overlay itself is regular MAUI UI (`ToPlatform`-hosted), deliberately detached from the page tree, so it never shows up in the inspected tree and works over any page, Shell or modal.
- The web panel is served by an `HttpListener` inside the app; the client is a dependency-free static page embedded in the assembly.
- Element bounds come from the native views (`GetLocationInWindow` / `ConvertRectToView` / `TransformToVisual`), so scrolling and transforms are reflected.
- HTTP interception is a plain `DelegatingHandler` — no proxy, no system certificates, nothing to trust.
- The inspector registers in the standard `IServiceCollection` and keeps every service to a single public constructor, so apps that swap the MAUI container (Autofac & co.) resolve it fine.

### Troubleshooting the connection

**The startup log says `self-probe on port N failed: …`** — the server bound the port but could
not reach itself over loopback; the message carries the underlying reason. A common Android trigger used to be
the cleartext policy; current versions probe with a handler that policy doesn't apply to, so take
the quoted reason at face value.

**The startup log says `port N is shadowed by another process`** — this one is real: something
answered the probe with a wrong instance id. Usually a previous run of the same app is still
alive; kill it or let the auto-assign pick the next port.

**The browser on your desktop can't connect (or spins forever) even though the app says
`web inspector listening`** — the URL is served from *inside* the app, so the browser's route to
it is what usually breaks:

- **Android emulator** — the emulator has its own network stack; without an `adb forward` nothing
  on the host answers `localhost:<port>`. `maui-inspector-sync` does this for you; by hand, map it
  onto a *shifted* host port, never 1:1.
- **Two apps answering one port** — the panel shows one app's header over another app's data, and
  the numbers jump every second. Both apps are right: an iOS **simulator** app runs as a host
  process and binds the Mac's port, while an Android app binds the same number inside the
  emulator's own loopback, where nothing collides. The app's startup probe cannot see across that
  line, so nothing moves out of the way — and then `adb forward tcp:P tcp:P` puts the two on one
  host port. Both listeners stay up (one wildcard, one on `127.0.0.1`) and requests go to them in
  turn. Check with `lsof -nP -iTCP:9295-9309 -sTCP:LISTEN` (macOS): two rows on one port is the
  proof. Fix: `adb forward --remove tcp:P`, then `adb forward tcp:1P tcp:P` and open
  `http://localhost:1P/` — or let `maui-inspector-sync` assign the host ports. The panel calls it
  out on its own (the connection dot turns red with *two apps on this port*) by watching the
  per-process nonce every answer carries.
- **A connection that hangs instead of being refused** — an iOS simulator app suspended in the
  background still accepts connections but never answers. Foreground it, or kill the stale app.
- **iOS simulator** — bring the app to the foreground: iOS suspends a backgrounded app together
  with its HTTP server, so the panel shows `app in background` and requests time out.
- **Physical devices** — `localhost` won't do; use the device's IP (Android additionally needs
  the `INTERNET` permission, present by default) or, on Android, `adb forward` over USB.

### Known limitations

- The soft keyboard can cover the on-device panel while typing on phones — drag the panel up by its header, or use the web panel.
- Full trimming/AOT of **release** builds may strip property setters used by the editors; the tool is intended for debug builds (wrap the registration in `#if DEBUG`).
- Native-only views (non-MAUI subviews) are not listed in the tree.
- Breakpoints hold a request until you continue it — mind your `HttpClient.Timeout`.
- Binary or very large (>128 KB) HTTP bodies are not captured and cannot be recorded into scenarios.
- The Windows implementation compiles only on Windows and has not been exercised as thoroughly as iOS/Android yet.
