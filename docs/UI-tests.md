# UI tests (Maestro, Appium)

A UI test needs two things from the inspector: **which rules exist** and **which scenario is
active** — decided before the app makes its first call, because a version check or a token refresh
fires during startup, long before a test step could run.

**0. Give DataTemplate rows unique AutomationIds.** Every row of a `CollectionView` /
`BindableLayout` comes from one XAML line, so a literal `AutomationId` cannot tell them apart —
but the items' data can. Select any element inside an items host and use **🆔** (next to the
AutomationId field, or in the tree's right-click menu): the dialog lists the item
`BindingContext`'s properties, marks which ones actually hold **unique values across the live
rows**, previews the resulting ids (`visit-101, visit-102, …`) and applies
`AutomationId="{Binding Id, StringFormat='visit-{0}'}"` to every instance — live and written
back to the template's XAML. The on-device panel has a one-tap variant that picks the best
unique property (`Id`-like names first) by itself.

**1. Ship the rules with the test build.** Record a flow in the panel, hit **⬆ Export**, and add the
file to the app project:

```xml
<MauiAsset Include="inspector-rules.json" LogicalName="inspector-rules.json" />
```
```csharp
builder.UseMauiInspector(options =>
{
    options.SeedRulesAsset = "inspector-rules.json";   // loaded only when the app has no rules
});
```

Because it travels inside the package, it survives `clearState` / a fresh install — which is what a
CI run does on every execution. It is imported **only when the rule registry is empty**, so a
developer's own rules are never overwritten.

**2. Pick the scenario per test with a launch argument.**

```yaml
# Maestro
- launchApp:
    clearState: true
    arguments:
      inspectorScenario: "qa-error"
```
```python
# Appium — iOS
options.process_arguments = {'args': ['-inspectorScenario', 'qa-error']}
# Appium — Android
options.optional_intent_arguments = '--es inspectorScenario qa-error'
```

| Value | Effect |
| --- | --- |
| a scenario name | that scenario becomes active, mocking on |
| `none` | global rules only |
| `off` | mocking suspended entirely, the app talks to the real API |
| *not passed* | **unchanged** — whatever the app had stored, exactly as without this feature |

The argument is applied **in memory only**: a test run never overwrites the scenario a developer
picked in the panel. It also outranks the `activeScenario` recorded in the seed file. Use
`inspectorRules` to name a different bundled file per test, when one build carries several sets.

**3. Change the scenario mid-flow over HTTP** (the panel's own API — see below):

```javascript
// Maestro runScript
http.post('http://localhost:9295/api/mock/rules/scenario', { body: JSON.stringify({ name: 'qa-offline' }) })
```

Pin the port for tests (`options.WebServerPort = 9295`) — the default scans 9295–9309. Android
emulators are reached through `maui-inspector-sync` (it forwards the ports for you) or a manual
`adb forward tcp:19295 tcp:9295`; physical devices need the device IP.
