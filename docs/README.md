# MAUI Inspector — documentation

Chrome DevTools for your .NET MAUI app: inspect and live-edit the visual tree, mock HTTP traffic,
hunt leaks and drive several devices at once — all from a web panel served by the app itself.

New here? Start with [Getting started](Getting-started.md) — two lines in `MauiProgram.cs` and the
app prints the panel's URL.

### Using the panel

- **[Getting started](Getting-started.md)** — packages, setup, options, manual control
- **[The web panel](Web-panel.md)** — the views and toolbars, and the on-device overlay
- **[Inspecting](Inspecting.md)** — the tree, the box model, the property sheet, live edits
- **[Styles & resources](Styles-and-resources.md)** — extract style, the Resources popup, the design cookbook
- **[WYSIWYG editor](WYSIWYG-editor.md)** — structure editing, toolbox, snap lines, grid designer
- **[Multi-device](Multi-device.md)** — one panel, several simulators, emulators and phones

### Writing changes back

- **[XAML Updater](XAML-Updater.md)** — the sync tool that lands panel edits in your `.xaml` files

### Diagnosing

- **[Network & HTTP mocking](Network-and-mocking.md)** — recording, rules, scenarios, breakpoints, offline replay
- **[Memory & leaks](Memory-and-leaks.md)** — live readings, leak snapshots, heap dumps with root paths

### Automating

- **[UI tests](UI-tests.md)** — Maestro and Appium: AutomationIds, mocks, a leak gate
- **[HTTP API](HTTP-API.md)** — every endpoint the panel itself uses

### The rest

- **[Reference](Reference.md)** — platforms, options, storage, how it works, troubleshooting, limitations

---

These pages are the source of truth; the [wiki](https://github.com/Immons/Tools.Maui.Inspector/wiki)
is a mirror, republished from `docs/` on every push to `main`. Edit them here — a change made in the
wiki directly is overwritten by the next mirror run.
