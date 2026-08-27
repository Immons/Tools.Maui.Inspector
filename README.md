<p align="center">
  <img src="https://raw.githubusercontent.com/Immons/Tools.Maui.Inspector/main/docs/inspector-logo.png" width="300" alt="MAUI Inspector logo"/>
</p>

<h1 align="center">MAUI Inspector</h1>

<p align="center">
  <a href="https://www.nuget.org/packages/Immons.Tools.Maui.Inspector"><img src="https://img.shields.io/nuget/v/Immons.Tools.Maui.Inspector.svg?label=Immons.Tools.Maui.Inspector" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/Immons.Tools.Maui.Inspector"><img src="https://img.shields.io/nuget/dt/Immons.Tools.Maui.Inspector.svg" alt="NuGet downloads"/></a>
  <a href="https://www.nuget.org/packages/Immons.Tools.Maui.Inspector.Sync"><img src="https://img.shields.io/nuget/v/Immons.Tools.Maui.Inspector.Sync.svg?label=Immons.Tools.Maui.Inspector.Sync" alt="XAML Updater"/></a>
  <a href="https://github.com/Immons/Tools.Maui.Inspector/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"/></a>
</p>

**Chrome DevTools for your .NET MAUI app.** Inspect and live-edit the visual tree, mock and
intercept HTTP traffic, and push the same edits to several devices at once — from a web panel
in your desktop browser, with an on-device overlay as the fallback when you have no laptop at hand.

Everything runs **inside your app**: no IDE integration, no proxy, no certificates.

- **Inspect & edit** the live visual tree — box model, properties, styles, spans, grids, `{Binding}` / `{StaticResource}` / `{OnPlatform}` — with every change written back to your XAML if you want it.
- **Edit the structure, WYSIWYG-style** — drag controls from a toolbox onto the live mirror, add / remove / reorder / reparent / wrap / unwrap / copy-paste elements with undo & redo, and it all lands in your `.xaml` files as real markup ([details](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/WYSIWYG-editor.md)).
- **Intercept HTTP** — record traffic with bodies, mock it with rules and scenarios, record a whole flow and replay it offline, or pause a call at a breakpoint and edit it.
- **Find leaks** — a Memory view with live readings, leak snapshots (which pages, views and view models outlived their window, with the MAUI-specific evidence) and one-click heap dumps through `dotnet-gcdump`, root paths included ([details](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Memory-and-leaks.md)).
- **Design like in a designer** — snap lines, alignment pins, a drag-to-resize grid designer, style extraction, an editable resources browser, a live XAML preview of the selection — and a **design cookbook**: the app's colors, fonts, styles, controls, images and templates as live samples, with a before/after diff of what a style edit changed ([details](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Styles-and-resources.md#the-design-cookbook)).
- **Drive several devices at once** — one panel updates the same app on every connected simulator, emulator or phone, and the header's device picker inspects any of them from a single portal.

## Install

```xml
<!-- Debug-only reference keeps the inspector out of release builds entirely -->
<PackageReference Include="Immons.Tools.Maui.Inspector" Version="0.9.18" Condition="'$(Configuration)' == 'Debug'" />
```

Then two lines in `MauiProgram.cs`:

```csharp
using Immons.Tools.Maui.Inspector;

#if DEBUG
builder.UseMauiInspector(options => options.EnableWebServer = true);
#endif
```

Run the app and open the URL it prints — the panel is served by the app itself:

```
[MauiInspector] web inspector listening on http://localhost:9295/
```

Targets `net10.0-ios`, `net10.0-android` and `net10.0-windows` (plus a no-op `net10.0`), MIT licensed.
Four optional companion packages (XAML write-back, SQLite storage, heap-dump diagnostics, markup
extensions) are listed in [Getting started](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Getting-started.md#packages).

![Web panel](https://raw.githubusercontent.com/Immons/Tools.Maui.Inspector/main/docs/web-inspector.png)

## Documentation

The full guide lives in [`docs/`](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs) — and, mirrored page for page, in the
[wiki](https://github.com/Immons/Tools.Maui.Inspector/wiki).

| Page | What is in it |
| --- | --- |
| [Getting started](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Getting-started.md) | Packages, the two-line setup, options, manual control |
| [The web panel](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Web-panel.md) | The panel's views and toolbars, and the on-device overlay |
| [Inspecting](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Inspecting.md) | The tree, the box model, the property sheet, live edits |
| [Styles & resources](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Styles-and-resources.md) | Extract style, the editable Resources popup, the design cookbook |
| [WYSIWYG editor](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/WYSIWYG-editor.md) | Structure editing, the toolbox, snap lines and the grid designer |
| [XAML Updater](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/XAML-Updater.md) | The sync tool that writes edits back into your `.xaml` files |
| [Network & HTTP mocking](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Network-and-mocking.md) | Recording, mock rules, scenarios, breakpoints, offline replay |
| [Memory & leaks](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Memory-and-leaks.md) | Live readings, leak snapshots, heap dumps with root paths |
| [Multi-device](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Multi-device.md) | One panel driving several simulators, emulators and phones |
| [UI tests](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/UI-tests.md) | Maestro and Appium: AutomationIds, mocks and a leak gate |
| [HTTP API](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/HTTP-API.md) | Every endpoint the panel itself uses |
| [Reference](https://github.com/Immons/Tools.Maui.Inspector/blob/main/docs/Reference.md) | Platforms, options, storage, how it works, troubleshooting, limitations |

## License

MIT — see [LICENSE](https://github.com/Immons/Tools.Maui.Inspector/blob/main/LICENSE).
