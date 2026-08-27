# Network & HTTP mocking

The **Network** and **Mocks** views only see traffic that flows through `MauiInspectorHttpHandler` —
a standard `DelegatingHandler` you add to your `HttpClient` pipeline. Everything routed through it
is recorded with full request/response bodies and can be mocked, delayed, failed or paused at
a breakpoint; anything that bypasses it stays invisible to the inspector.

**Plain `HttpClient`** — the parameterless constructor brings its own `HttpClientHandler`:

```csharp
using Immons.Tools.Maui.Inspector;

var client = new HttpClient(new MauiInspectorHttpHandler());
```

**Keeping your existing handler chain** — pass it as the inner handler. The inspector sees the
request first (so a mock short-circuits the whole chain), then your handlers run unchanged:

```csharp
var client = new HttpClient(
    new MauiInspectorHttpHandler(
        new AuthTokenHandler(new HttpClientHandler())));
```

**`IHttpClientFactory` / typed clients / Refit** — register it with
`MauiInspectorHttpHandler.ForClientFactory`. Do **not** `new` the handler here: the factory
requires `InnerHandler` to be left unassigned and the constructors assign it, so
`AddHttpMessageHandler(() => new MauiInspectorHttpHandler())` throws
an `InvalidOperationException` on the first request. `ForClientFactory()` leaves it unassigned:

```csharp
var api = builder.Services.AddHttpClient<GitHubApiClient>(
    client => client.BaseAddress = new Uri("https://api.github.com"));

#if DEBUG
api.AddHttpMessageHandler(MauiInspectorHttpHandler.ForClientFactory);
#endif
```

The same `IHttpClientBuilder` call works for named clients (`AddHttpClient("api")`) and
Refit registrations (`AddRefitClient<IGitHubApi>()`). Register it **last** so the inspector
sits outermost and records exactly what your other handlers (auth headers, retries) produced.
The `#if DEBUG` guard matches the Debug-only `PackageReference` from
[Packages](Getting-started.md#packages) — release builds compile without the inspector at all.

**Logs** — stream `ILogger` output into the panel's Logs view:

```csharp
// register AFTER any ClearProviders()
builder.Logging.AddMauiInspector();
```

### The Network and Mocks views

**Network — requests, breakpoints and bodies**

![Network requests](web-network.png)

Every call that goes through `MauiInspectorHttpHandler` is recorded in an aligned table
(time · status · method · URL · duration · size · matched rule) with full request and
response bodies (click a row to expand). Breakpoints pause matching requests or responses so you
can edit the body or status and continue — Proxyman-style, but inside the process, so TLS and
certificate pinning are none of your concern.

**Mocks — rules, scenarios and recording**

![Mock rules and scenarios](web-mocks.png)

Rules match on method + URL pattern (the most specific rule wins) and can replace the request or
response body, force a status, add a delay, simulate a timeout or a network error, or answer
completely without the network. Group them into **scenarios** ("premium user", "empty portfolio",
"force update"), switch the active one from a picker, or hit **⏺ Record**, click through a flow and
turn the whole request path into a replayable scenario. Rules survive app restarts, so even
a version check fired on startup is already mocked.

### Feature summary

- **Recording** — method, status, timing, size and full request/response bodies for every call through `MauiInspectorHttpHandler`, with a filter over method/URL/status/tag and a **🧹 Clear** button to start from a clean slate.
- **Mock rules** — method + URL pattern (substring or `*` wildcard; the most specific rule wins) → replace request/response body, force a status, delay, simulate timeout/network error, or answer entirely without the network.
- **Scenarios** — named rule groups; a rule can belong to many, one picker switches the active one, and rules of the active scenario outrank global ones. The same picker has an **off** entry that suspends mocking entirely — global rules included — so you can compare against the real API and switch back without touching a single rule. It is remembered across restarts.
- **Recording into a scenario (⏺)** — record a flow, stop, and every unique call becomes a no-network rule tagged with the new scenario.
- **Breakpoints (⏸)** — pause requests and/or responses matching a filter, edit body/status, continue or abort.
- **Portable** — the whole state (scenarios + rules) exports/imports as one JSON file and persists on the device between runs; the browser also keeps a per-app backup and restores it after a reinstall.
- **Scenarios reach your code** — `MauiInspector.IsScenarioActive("offline")` / `MauiInspector.ActiveScenario` let debug builds fake what HTTP interception cannot see (an MSAL sign-in, a native SDK, a sensor), so one picker can put the whole app offline.

### Offline testing

HTTP interception covers everything that goes through `MauiInspectorHttpHandler`, but not what
happens outside your process — an MSAL/OAuth sign-in runs in the system browser, and libraries with
their own `HttpClient` bypass the handler until you route them through it
(`.WithHttpClientFactory(…)` for MSAL). The scenario API bridges that gap:

```csharp
#if DEBUG
if (MauiInspector.IsScenarioActive("offline"))
{
    // skip the real sign-in; every API call is answered by the scenario's rules
    var user = await _users.CreateUser("offline-token");
    return new AuthenticationResult(AuthenticationStatus.Authenticated, user);
}
#endif
```

Recipe: **⏺ Record** a full flow once online → **⏹ Stop** and name it `offline` → add the snippet
above → from then on selecting that scenario runs the whole app with no network and no login.
