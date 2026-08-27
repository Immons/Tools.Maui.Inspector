# HTTP API

Everything the panel does goes through this API, so anything the panel can do, a script can do too.
All POST bodies are JSON. Base URL is the one printed at startup (`MauiInspector.WebServerUrl`).

**Inspecting**

| Method & path | What it does |
| --- | --- |
| `GET /api/ping` | App name, device, instance id and the inspector's package version |
| `GET /api/tree` | Visual tree as JSON |
| `GET /api/dump` | The tree as plain text |
| `GET /api/selection` | Currently selected element with its properties |
| `POST /api/element/{id}/select` | Select an element |
| `POST /api/element/{id}/property` | Set a property — `{section, name, value}` or `{section, name, clear: true}`; the value accepts `{Binding …}`, `{StaticResource …}`, `{OnPlatform …}` |
| `POST /api/element/{id}/action` | Structural action (hide, remove, duplicate…), same body shape |
| `GET /api/history` · `POST /api/history/undo` | Applied edits; undo the last one |
| `GET /api/changes` | Edits pending write-back to XAML |
| `GET /api/measure` · `POST /api/clear` | Distance between two elements; clear the measurement |
| `GET /api/screenshot` · `POST /api/select-at` | Device mirror image; select by screen coordinates |
| `GET /api/cookbook` | The design cookbook catalog: sections and items, what the device has built, the theme |
| `POST /api/cookbook/open` | `{on, section?, page?, item?}` — push / pop the cookbook page on the device and steer what it shows |
| `GET /api/cookbook/preview?id=…` | PNG of one cookbook tile (rendered on a stage when not on screen); `X-Visual-States` header lists its states |
| `POST /api/cookbook/focus` | `{id}` — single out the item at full width: headless on the off-screen stage, or on a page of its own when the gallery is open on the device; `null` drops it. `preview?id=…&focus=1` captures that instance |
| `POST /api/cookbook/state` | `{id, state}` — force a visual state on the sample the device shows (the focused one, else its tile) |
| `GET /api/theme` · `POST /api/theme` | `{theme: "system" \| "light" \| "dark"}` — the app-wide theme override |

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
