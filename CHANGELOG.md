# Changelog

## Unreleased

- SignalR view: new package `Immons.Tools.Maui.Inspector.SignalR` with one line on the `HubConnectionBuilder` — `.WithMauiInspector("chat")`, in any position of the chain, inert unless `UseMauiInspector` was called. The web panel's new 📡 SignalR tab shows each hub's state (Connecting / Connected / Reconnecting / Disconnected) with transport, protocol, connection id, traffic counters, last ping, last error and a reconnect counter, plus a timeline of messages in both directions (invocations, stream items, completions with invoke → completion time, pings, close, retries and failed attempts) with filters by hub and text, pause, clear and formatted details. Arguments are masked (token, password, authorization, bearer strings, JWTs…) and capped at 4 KB before they are stored; a ring buffer keeps 1000 events per hub. Works with JSON, Newtonsoft and MessagePack; the bytes on the wire are never changed.
- SignalR: 💉 Inject — a developer tool that delivers a server → client call to the app's `On(...)` handler as if the server had sent it (text protocols, not with stateful reconnect; `AllowInjection = false` turns it off).
- HTTP API: `GET /api/signalr`, `GET /api/signalr/events?hub=&since=&method=&limit=`, `GET /api/signalr/event?seq=`, `POST /api/signalr/pause`, `/clear`, `/inject`.
- Sample: a SignalR demo page and a small SignalR server (`samples/Immons.Tools.Maui.Inspector.SignalRServer`); the iOS sample uses the UIKit scene lifecycle required by the iOS 27 SDK.
- Web panel smoke tests: `CDP_PORT` / `CDP_USER_DATA_DIR` let `cdp.mjs` drive an installed Chrome; `signalr.smoke.mjs` walks the SignalR view.

## 0.9.21

- Memory: Watch mode is now on by default (web panel button and on-device 🧠 Mem pane show it on after connecting). Switch it off with the button or `options.Memory.WatchNavigation = false`. Release builds are unaffected as long as the package is referenced Debug-only.
