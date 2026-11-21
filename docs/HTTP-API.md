# HTTP API

Everything the panel does goes through this API, so anything the panel can do, a script can do too.
All POST bodies are JSON. Base URL is the one printed at startup (`MauiInspector.WebServerUrl`).

**Inspecting**

| Method & path | What it does |
| --- | --- |
| `GET /api/ping` | App name, device, instance id, the inspector's version — and for discovery: `deviceName`, `model`, `osVersion`, `appId`, `appVersion`, `port`, `addresses`/`urls` (the device's LAN addresses), `window` (size and pixel `scale`), `remoteControl` |
| `GET /api/tree` | Visual tree as JSON |
| `GET /api/dump` | The tree as plain text |
| `GET /api/selection` | Currently selected element with its properties |
| `POST /api/element/{id}/select` | Select an element |
| `POST /api/element/{id}/property` | Set a property — `{section, name, value}` or `{section, name, clear: true}`; the value accepts `{Binding …}`, `{StaticResource …}`, `{OnPlatform …}` |
| `POST /api/element/{id}/action` | Structural action (hide, remove, duplicate…), same body shape |
| `GET /api/history` · `POST /api/history/undo` | Applied edits; undo the last one |
| `GET /api/changes` | Edits pending write-back to XAML |
| `GET /api/measure` · `POST /api/clear` | Distance between two elements; clear the measurement |
| `GET /api/screenshot` · `POST /api/select-at` | Device mirror image (JPEG, physical pixels — divide by `window.scale` for window units); select by window coordinates |
| `GET /api/cookbook` | The design cookbook catalog: sections and items, what the device has built, the theme |
| `POST /api/cookbook/open` | `{on, section?, page?, item?}` — push / pop the cookbook page on the device and steer what it shows |
| `GET /api/cookbook/preview?id=…` | PNG of one cookbook tile (rendered on a stage when not on screen); `X-Visual-States` header lists its states |
| `POST /api/cookbook/focus` | `{id}` — single out the item at full width: headless on the off-screen stage, or on a page of its own when the gallery is open on the device; `null` drops it. `preview?id=…&focus=1` captures that instance |
| `POST /api/cookbook/state` | `{id, state}` — force a visual state on the sample the device shows (the focused one, else its tile) |
| `GET /api/theme` · `POST /api/theme` | `{theme: "system" \| "light" \| "dark"}` — the app-wide theme override |

**Remote control** — drive the app from a script or an agent, on a simulator or a physical device
(details and examples [below](#remote-control)):

| Method & path | What it does |
| --- | --- |
| `POST /api/tap` | `{x, y}` or a selector — taps like a finger |
| `POST /api/long-press` | `{x, y}` or a selector, `durationMs` (800) |
| `POST /api/swipe` | `{x1, y1, x2, y2, durationMs}`, or `{direction, amount}` with an optional selector |
| `POST /api/scroll` | `{direction, amount, gesture?}` and an optional selector of the container |
| `POST /api/scroll-to` | selector of the target, `direction`, `maxScrolls`, optional `container` selector |
| `POST /api/text` | `{value, clear?, submit?, dismiss?}` and a selector (else the focused input) |
| `POST /api/key` | `{text}` or `{key}` (`Backspace`, `Delete`, `Enter`, `ArrowLeft`…) into the focused input |
| `POST /api/back` | The platform back: popup → modal → navigation/Shell |
| `GET /api/find` | `?automationId=` / `text=` / `placeholder=` / `type=` / `id=` — every match with bounds, visibility, enabled, focus |
| `POST /api/wait` | selector, `timeoutMs` (10 000), `gone?` — until it shows up (or disappears); 408 on timeout |
| `GET /api/page` | Current page (type, title), navigation and modal stacks, popups, tabs, window size and scale, focused input |

**Toggles** — each takes `POST {on: bool}`: `/api/measure-mode`, `/api/select-mode`, `/api/overlay`,
`/api/debug-paint`, `/api/perf`, `/api/slow-animations`, `/api/wysiwyg`.

**Network**

| Method & path | What it does |
| --- | --- |
| `GET /api/network` | Recorded calls (newest first), without bodies |
| `GET /api/network/body?seq=N` | Request and response body of one call |
| `POST /api/network/clear` | Drop the recorded calls |
| `GET /api/memory` | Memory readings (current + recent), tracking and heap-dump state |
| `POST /api/memory/snapshot` | Run a leak snapshot (`GET` returns the last one) |
| `POST /api/memory/gc` | Force a collection round |
| `GET /api/memory/peers` | Java peer census (Android) |
| `POST /api/memory/heapdump` | Order a heap dump **and wait for it**: returns the finished report (`{timeoutMs?, types?}`) — one call for a script |
| `POST /api/memory/dump/request` | Order a heap dump without waiting; `GET /api/memory/dumps` lists the jobs (without reports) |
| `GET /api/memory/dump/report?id=N` | One job's report — reports are megabytes, so they are fetched on demand |
| `POST /api/memory/dump/trace` | `{jobId, type}` — root paths of one more type from an existing dump |
| `POST /api/memory/alloc/request` | `{seconds}` — record allocations with dotnet-trace |
| `POST /api/memory/baseline` | `{clear?}` — mark (or clear) the state everything is measured against |
| `POST /api/memory/settings` | `{watch, disconnectHandlersOnPop, clearBindingContextOnPop}` — the runtime switches |
| `GET /api/memory/ledger` · `/api/memory/snapshots` · `/api/memory/images` | Navigation ledger, snapshot history, decoded images |
| `GET /api/intercept` | Breakpoint config and the calls currently paused |
| `POST /api/intercept/config` | `{req, resp, filter}` — which phases pause, on which URLs |
| `POST /api/intercept/resume` | `{id, body?, status?}` — continue a paused call, optionally rewritten |
| `POST /api/intercept/abort` | `{id}` — fail a paused call |

**Mocks**

| Method & path | What it does |
| --- | --- |
| `GET /api/mock/rules` | Rules, scenario list, active scenario, `mockingEnabled`, recording state |
| `POST /api/mock/rules/save` | Add (`id: 0`) or replace (`id > 0`) one rule |
| `POST /api/mock/rules/delete` | `{id}` |
| `POST /api/mock/rules/enable` | `{id, enabled}` — toggle one rule |
| `POST /api/mock/rules/import` | `{scenarios, activeScenario, rules}` — a whole set in one write |
| `POST /api/mock/rules/mocking` | `{enabled}` — master switch (the picker's **off**) |
| `POST /api/mock/rules/scenario` | `{name}` — activate a scenario (`""` = global rules only) |
| `POST /api/mock/rules/scenario/add` · `/remove` | `{name}` — manage the scenario registry |
| `POST /api/mock/record/start` · `/stop` · `/cancel` | Record traffic into a new scenario |

**Multi-device** — `POST /api/broadcast/property` and `/api/broadcast/action` take
`{source, elementName, automationId, type, page, section, name, value}` and apply the edit to this
app on every connected device, matched by XAML source identity with the name/page fallbacks.

**Logs** — `GET /api/logs` returns what `builder.Logging.AddMauiInspector()` collected.

**SignalR** — hubs registered with `.WithMauiInspector("name")` (package
`Immons.Tools.Maui.Inspector.SignalR`, see [SignalR](SignalR.md)). Payloads are already masked and
cut to `MaxPayloadChars` when they are recorded; the API never sees the originals.

| Method & path | What it does |
| --- | --- |
| `GET /api/signalr` | `{paused, lastSeq, hubs: [...]}` — per hub: `name`, `state` (`Connecting` · `Connected` · `Reconnecting` · `Disconnected`), `stateSince` (unix ms), `connectionId`, `url`, `protocol`, `transport`, `reconnects`, `attempt`, `lastError`, `instances`, `messagesIn/Out`, `bytesIn/Out`, `lastPingIn/Out`, `events`, `capacity`, `dropped`, `injectBlocker` (null = injection possible) |
| `GET /api/signalr/events` | `?hub=` (exact), `since=` (sequence number, exclusive), `method=` (substring, case-insensitive), `limit=` (default 500, max 5000 — the newest win). Returns `{lastSeq, events: [...]}` oldest first; each event: `seq`, `hub`, `time` (unix ms), `dir` (`out` = client → server, `in` = server → client, `""` = lifecycle), `kind` (`invocation` · `stream-invocation` · `stream-item` · `completion` · `cancel` · `ping` · `close` · `ack` · `sequence` · `state` · `retry` · `error`), and when known `method`, `invocationId`, `bytes`, `ms` (invoke → completion), `state`, `attempt`, `error`, `connectionId`, `injected`, `truncated`, `preview` (first 140 characters of the payload) |
| `GET /api/signalr/event?seq=N` | One event with its full (masked, size-capped) `payload`; 404 when it was dropped from the buffer |
| `POST /api/signalr/pause` | `{on}` — stop/resume recording messages; state changes and counters keep going. Without `on` it toggles |
| `POST /api/signalr/clear` | `{hub?}` — drop the events and counters of one hub, or of all |
| `POST /api/signalr/inject` | `{hub, method, args}` — **developer tool**: deliver `method(args…)` to the app's `On(...)` handler as if the server had sent it. `args` is a JSON array (or its text); a single value is one argument. 200 `{ok:true}` once queued, 400 `{ok:false, error}` when the hub is unknown, not connected, uses a binary protocol or stateful reconnect, or the JSON is invalid |

```bash
B=http://localhost:9295
curl -s $B/api/signalr | jq '.hubs[] | {name, state, reconnects}'
curl -s "$B/api/signalr/events?hub=chat&method=SendMessage&since=0" | jq '.events[] | {dir, kind, method, bytes, ms}'
curl -s -X POST $B/api/signalr/inject -d '{"hub":"chat","method":"ReceiveMessage","args":[{"text":"hi","from":"panel"}]}'
```

## Remote control

The input endpoints let a script — a shell loop, a test runner, an AI agent — operate the app the way
a person would, over plain HTTP. They work with any `Activation` (also `None`: the on-device panel
never has to be opened) and need `EnableWebServer = true`; `EnableRemoteControl = false` switches them
off (403) while keeping the reading endpoints.

**Reaching the device.** The server binds `http://*:{port}` (9295 upwards, see `GET /api/ping` → `port`).

- *iOS simulator* — the app listens on the Mac itself: `http://localhost:9295`.
- *Physical iPhone/iPad on the same Wi-Fi* — `http://<device-ip>:9295`; the IP is in Settings → Wi-Fi,
  in the app's startup log, or found by `maui-inspector-sync discover` / `tools/inspector-remote.sh discover
  192.168.1` (both sweep the /24 for `/api/ping`). `GET /api/discover` makes a running app do the same sweep of
  its own subnet and returns `{found:[{url,label,instance}],hosts,ms}` — the panel's *Scan local network…* uses it. Over USB instead: `iproxy 9295 9295` (libimobiledevice) and use `localhost`. If the phone
  does not answer, check the app's *Local Network* permission (iOS asks the first time the app talks to
  the LAN) and that the Wi-Fi does not isolate clients.
- *Android* — `adb forward tcp:19295 tcp:9295` (or let `maui-inspector-sync` do it), device IP on Wi-Fi.

**Selectors.** Every endpoint that targets an element takes the same fields; all given ones must match:

| Field | Matches |
| --- | --- |
| `automationId` | `AutomationId` (case-insensitive); on iOS also the accessibility identifier of native views (tab bar buttons…) |
| `text` | What the element shows: Label (spans included), Button, Entry/Editor text or placeholder, RadioButton, Picker, `SemanticProperties.Description`, and a `Text`/`Hint`/`Placeholder`/`Title` string property of custom controls. Case- and whitespace-insensitive; exact matches win over substrings |
| `placeholder` | Entry/Editor/SearchBar placeholder, Picker title |
| `type` | Type name or a base type's (`Entry` also finds subclasses) |
| `id` | The element id from `/api/tree` or `/api/find` |
| `index` | Which match (0 = the first one a finger can reach) |
| `exact` | `true` = no substring matches |

Lookups search what is on screen: Mopups popups first, then modal pages, then the window's page — and
inside tabbed / navigation / flyout / Shell pages only the current page. Elements inside
CollectionView/ScrollView cells are found like any other. When nothing in the MAUI tree matches an
`automationId`/`text`, native views are searched (on iOS: tab bar buttons, navigation bar items,
`DisplayAlert` buttons). `index` 0 skips matches something else covers (the page under a modal).

**Taps are touches.** On iOS and Android the tap is injected into the platform's touch pipeline at the
element's center — hit-testing, gesture recognizers, UIControl tracking, CollectionView selection and
scroll views see a real finger. On iOS this synthesizes `UITouch` events the way UI-test drivers such as
KIF do, through private UIKit API: fine for debug and test builds, never for an App Store build
(`NativeTouchInjection = false` turns it off). Without injection (Windows, or switched off) a tap invokes
the element's own reaction instead — gesture recognizers, button clicks, toggles, focusing inputs,
selecting the CollectionView item — and `native: false` asks for that path on purpose. Responses say
which path ran: `"via": "touch" | "semantic" | "native"`. An element that is off screen inside a scroll
container is scrolled to its center before the tap (`scroll: false` prevents that).

```bash
B=http://localhost:9295
curl -s $B/api/ping                                                   # which device/app is this
curl -s $B/api/page                                                   # where am I
curl -s -X POST $B/api/tap  -d '{"text":"emailem i hasłem"}'          # tap by visible text
curl -s -X POST $B/api/text -d '{"text":"Email","value":"me@example.com"}'   # caption of a field → its Entry
curl -s -X POST $B/api/text -d '{"automationId":"PasswordEntry","value":"secret","submit":true}'
curl -s -X POST $B/api/tap  -d '{"text":"Kontynuuj"}'
curl -s -X POST $B/api/wait -d '{"text":"Kontynuuj","gone":true,"timeoutMs":20000}'
curl -s -X POST $B/api/tap  -d '{"automationId":"Tab_ChatsViewModel"}'      # a native tab bar button
curl -s -X POST $B/api/scroll -d '{"direction":"down","amount":0.8}'        # programmatic, reports "moved"
curl -s -X POST $B/api/scroll -d '{"direction":"down","gesture":true}'      # a real drag instead
curl -s -X POST $B/api/scroll-to -d '{"text":"Ustawienia","maxScrolls":30}'  # page a list until it shows
curl -s -X POST $B/api/swipe -d '{"x1":200,"y1":400,"x2":200,"y2":750,"durationMs":250}'
curl -s -X POST $B/api/swipe -d '{"text":"Wiadomość","direction":"left"}'   # e.g. reveal SwipeView items
curl -s -X POST $B/api/long-press -d '{"text":"Kapcie","durationMs":900}'
curl -s -X POST $B/api/back
curl -s "$B/api/find?text=Czat&exact=1"
curl -s -o screen.jpg $B/api/screenshot
```

Details:

- **Coordinates** are window units (points on iOS, dp on Android) — `/api/find` returns them; the
  screenshot is in physical pixels (`window.scale` × larger).
- **`/api/swipe` `direction`** is the way the *finger* moves (`left` reveals a SwipeView's right items);
  **`/api/scroll` `direction`** is the way the *content* goes (`down` shows what is below). `amount` up
  to 1 is a fraction of the viewport, larger values are units. Without a selector, swipes and scrolls use
  the largest scrollable area on screen.
- **`/api/scroll`** sets the scroll container's offset directly (exact, no fling; `moved: false` means
  the end was reached); `gesture: true` drags it with an injected touch instead.
- **`/api/text`** focuses the input, replaces its text (`clear: false` appends), puts the cursor at the
  end, `submit: true` raises `Completed` / the search action, `dismiss: true` unfocuses (hides the
  keyboard). The selector may point at the input itself, at a container holding it (a custom field) or
  at the field's caption next to it. Password values are masked in the response.
- **`/api/back`** pops the topmost Mopups popup, else gives the top modal page its back handling (a
  NavigationPage inside pops) or pops the modal, else sends the back button to the page — `OnBackButtonPressed`
  overrides get their say, as with the Android button. The response's `action` names what happened.
- **Errors** are JSON `{ok: false, error}` with a status: 400 bad request, 403 remote control disabled,
  404 no match, 408 wait timeout, 409 element found but not on screen, 422 no input at the selector,
  501 the gesture needs touch injection.

**Limits.** Only the app's own UI is reachable: system UI drawn by iOS/Android outside the process —
permission prompts (camera, microphone, notifications, location), CallKit's incoming-call screen, the
share sheet, Photos/document pickers, the keyboard itself, Control Center — cannot be tapped or seen in
`/api/find` (the screenshot does not include them either). Grant permissions up front (`xcrun simctl
privacy <device> grant camera <bundle-id>`, `adb shell pm grant …`) or tap them by hand. In-app
`DisplayAlert`/action sheets are reachable. WebViews receive the touches, but their DOM is not searchable.

**Security.** The server has no authentication — anyone who can reach the port can read the UI and,
with remote control, operate the app. Keep it to debug/test builds:

```csharp
#if DEBUG
builder.UseMauiInspector(o => o.EnableWebServer = true);
#endif
// or, when the inspector ships in a test flavor: o.EnableRemoteControl = false; o.NativeTouchInjection = false;
```

A helper for the Mac side wraps the common calls: `tools/inspector-remote.sh` (`discover`, `ping`,
`page`, `shot`, `tap-text`, `tap-id`, `text-id`, `text-ph`, `wait-text`, `wait-gone`, `scroll`,
`scroll-to-text`, `swipe`, `long-press-text`, `back`; `INSPECTOR=http://host:port` picks the app).
