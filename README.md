<p align="center">
  <img src="docs/inspector-logo.png" width="300" alt="MAUI Inspector logo"/>
</p>

<h1 align="center">MAUI Inspector</h1>

<p align="center">
  <a href="https://www.nuget.org/packages/Immons.Tools.Maui.Inspector"><img src="https://img.shields.io/nuget/v/Immons.Tools.Maui.Inspector.svg?label=Immons.Tools.Maui.Inspector" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/Immons.Tools.Maui.Inspector"><img src="https://img.shields.io/nuget/dt/Immons.Tools.Maui.Inspector.svg" alt="NuGet downloads"/></a>
  <a href="https://www.nuget.org/packages/Immons.Tools.Maui.Inspector.Sync"><img src="https://img.shields.io/nuget/v/Immons.Tools.Maui.Inspector.Sync.svg?label=Immons.Tools.Maui.Inspector.Sync" alt="XAML Updater"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"/></a>
</p>

**Chrome DevTools for your .NET MAUI app.** Inspect and live-edit the visual tree, mock and
intercept HTTP traffic, and push the same edits to several devices at once — from a web panel
in your desktop browser, with an on-device overlay as the fallback when you have no laptop at hand.

Everything runs **inside your app**: no IDE integration, no proxy, no certificates.

- **Inspect & edit** the live visual tree — box model, properties, styles, spans, grids, `{Binding}` / `{StaticResource}` / `{OnPlatform}` — with every change written back to your XAML if you want it.
- **Edit the structure, WYSIWYG-style** — drag controls from a toolbox onto the live mirror, add / remove / reorder / reparent / wrap / unwrap / copy-paste elements with undo & redo, and it all lands in your `.xaml` files as real markup ([details](#wysiwyg-editor)).
- **Intercept HTTP** — record traffic with bodies, mock it with rules and scenarios, record a whole flow and replay it offline, or pause a call at a breakpoint and edit it.
- **Find leaks** — a Memory view with live readings, leak snapshots (which pages, views and view models outlived their window, with the MAUI-specific evidence) and one-click heap dumps through `dotnet-gcdump`, root paths included ([details](#memory--leaks)).
- **Design like in a designer** — snap lines, alignment pins, a drag-to-resize grid designer, style extraction, an editable resources browser, a live XAML preview of the selection — and a **design cookbook**: the app's colors, fonts, styles, controls, images and templates as live samples, with a before/after diff of what a style edit changed ([details](#the-design-cookbook)).
- **Drive several devices at once** — one panel updates the same app on every connected simulator, emulator or phone, and the header's device picker inspects any of them from a single portal.

## Table of contents

- [Getting started](#getting-started) — packages, two-line setup, manual control
- [The web panel](#the-web-panel) · [On the device](#on-the-device)
- [Inspecting](#inspecting) · [Editing properties](#editing-properties)
- [Styles & resources](#styles--resources) — extract style, the editable Resources popup, the design cookbook
- [WYSIWYG editor](#wysiwyg-editor) — structure editing, toolbox, designer aids
- [XAML Updater (sync tool)](#xaml-updater-sync-tool) — writing edits back to your sources
- [Network & HTTP mocking](#network--http-mocking) — recording, mocks, scenarios, offline testing
- [Memory & leaks](#memory--leaks) — live readings, leak snapshots, heap dumps with root paths
- [Multi-device](#multi-device) · [UI tests](#ui-tests-maestro-appium) · [HTTP API](#http-api)
- [Reference](#reference) — platforms, options, storage, how it works, troubleshooting, limitations

## Getting started

### Packages

| Package | What it is | Install |
| --- | --- | --- |
| [`Immons.Tools.Maui.Inspector`](https://www.nuget.org/packages/Immons.Tools.Maui.Inspector) | The inspector itself — add it to your MAUI app. | `dotnet add package Immons.Tools.Maui.Inspector` |
| [`Immons.Tools.Maui.Inspector.Sync`](https://www.nuget.org/packages/Immons.Tools.Maui.Inspector.Sync) | The **XAML Updater** dotnet tool that writes panel edits back into your `.xaml` files (optional). | `dotnet tool install -g Immons.Tools.Maui.Inspector.Sync` |
| [`Immons.Tools.Maui.Inspector.Persistency`](https://www.nuget.org/packages/Immons.Tools.Maui.Inspector.Persistency) | SQLite storage backend — worth adding once recorded scenarios grow large (optional). | `dotnet add package Immons.Tools.Maui.Inspector.Persistency` |
| [`Immons.Tools.Maui.Inspector.Diagnostics`](https://www.nuget.org/packages/Immons.Tools.Maui.Inspector.Diagnostics) | Build-only: gives Debug Android/iOS builds the diagnostic port the Memory view's **heap dumps** need — nothing to configure (optional). | `dotnet add package Immons.Tools.Maui.Inspector.Diagnostics` |

```xml
<!-- Debug-only reference keeps the inspector out of release builds entirely -->
<PackageReference Include="Immons.Tools.Maui.Inspector" Version="0.9.18" Condition="'$(Configuration)' == 'Debug'" />
```

Targets `net10.0-ios`, `net10.0-android` and `net10.0-windows` (plus a no-op `net10.0`), MIT licensed.

### Enable the inspector

In `MauiProgram.cs` — ideally only for debug builds:

```csharp
using Immons.Tools.Maui.Inspector;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>();

#if DEBUG
builder.UseMauiInspector(options =>
{
    options.EnableWebServer = true;                        // desktop web panel
    options.LongPressDuration = TimeSpan.FromMilliseconds(800);
    // options.WebServerPort = 9295;                       // force a port (default: auto)
    // options.LongPressTouchCount = 2;                    // avoid clashing with app long-presses
    // options.ShakeToOpen = true;                         // shake the device to open the overlay
    // options.MaxCapturedBodyBytes = 4 * 1024 * 1024;     // largest HTTP body kept for the Network view
});
#endif
```

The web server picks a free port from **9295–9309** and prints the URL to the platform console
and to the panel's Logs view:

```
[MauiInspector] web inspector listening on http://localhost:9296/ (auto-assigned)
```

`MauiInspector.WebServerUrl` returns the same URL at runtime (handy for a debug label in your app).

- **iOS simulator** — open that URL on the Mac.
- **Android emulator** — run `maui-inspector-sync`: it finds every connected device, forwards each app onto a free host port and prints the URL. By hand it is `adb forward tcp:1<port> tcp:<port>` — a *shifted* host port, never 1:1, because an iOS simulator app may already hold that number on the Mac.
- **Physical devices** — use the device IP (Android needs the `INTERNET` permission, present by default).

### Manual control

```csharp
MauiInspector.Show();          // open the on-device overlay
MauiInspector.Hide();
MauiInspector.Toggle();
MauiInspector.Inspect(someVisualElement);  // open with a specific element selected
MauiInspector.ShowCookbook();              // the design cookbook page (see Styles & resources)
```

## The web panel

Turn it on with two lines (see [Getting started](#getting-started)), open the printed URL on your
desktop, and you get the full inspector in a browser — while the app runs on a simulator,
an emulator or a physical device.

![Web inspector](docs/web-inspector.png)

The right side of the header shows the device the panel is talking to, with a green dot while the
connection is alive. When the app stops, restarts on another port or loses its `adb forward`, the
dot turns red and the label reads `disconnected` — previously the panel kept accepting clicks that
quietly went nowhere. A third, amber state covers the case that looks identical from the outside:
iOS suspends a backgrounded app **including its HTTP server**, so requests neither succeed nor fail,
they simply never return. The panel times those out and says `app in background` instead of staying
green on stale data. The **Devices** view lists each target with its address and marks the ones
that no longer answer, with one button to drop them (ports are recycled between runs, so stale
entries accumulate).

The header shows which package build is running (`v0.9.18`) next to the title. The panel also asks
nuget.org for the newest published version and turns that into `v0.9.18 → 0.9.19 available` when you
are behind — a plain GET of a public index, silently skipped when there is no connection.

A **device picker** next to the title points the whole panel — tree, properties, mirror,
resources, history — at any other running instance it can find (same scan as the Devices view).
Comparing the tablet and the phone rendition of a screen no longer needs two tabs: pick the other
device, inspect, pick *This device* to come back.

The tree, the property sheet and the device stay in sync both ways: click an element in the
browser and it highlights on the device; long-press on the device and the browser follows.
Single-key shortcuts toggle the modes — **S** Select, **M** Measure, **G** Guides, **P** on-device
Panel (plus `Ctrl/Cmd+Z` undo, `Ctrl/Cmd+C/V` copy & paste, Delete to remove the selection). The
properties list keeps its scroll position when the selection changes, so comparing the same
section across elements doesn't mean scrolling down again. Property edits apply **live** — and,
with the XAML Updater running, they are
[written back into your XAML sources](#xaml-updater-sync-tool).

The other views are covered in their own chapters: [Cookbook](#the-design-cookbook) (the app's
design system as live samples), [Network & mocks](#network--http-mocking), **Logs** (streams
`ILogger` output) and [Devices](#multi-device) for multi-device hot reload.

## On the device

No laptop? The same inspector runs as an overlay inside the app — long-press anything to inspect it.

| Box model + properties | Visual tree | Per-platform editing |
| --- | --- | --- |
| ![Box model and properties](docs/device-boxmodel.png) | ![Visual tree](docs/device-tree.png) | ![OnPlatform editor on the device](docs/device-live-edit.png) |

The on-device panel is feature-matched with the web one: live editors with `⋔` per-platform /
per-idiom composer, `✕` clear, `⛓︎`/`⋔︎` badges for bound and per-device values, and a `⋯` row with
**Guides**, **XAML** write-back, **Perf** and **Slow** toggles.

## Inspecting

- **Visual tree** — the whole window, auto-expanded to the selection, with type names, `x:Name`/`StyleId`, text snippets and child counts. Search by type, `@x:Name`, `#AutomationId` or text (spans included); arrow keys walk the tree.
- **Element picking** — with select mode (⌖) a single tap on the device picks an element; the hit test walks the real MAUI tree (through Shell intermediaries) in paint order.
- **Box model overlay** — margin (orange), padding (green) and content (blue) fills, dashed alignment guides and a dimensions badge, drawn over the live app.
- **Property sheet** — grouped sections (Element, Style, Bounds, Layout, Text, Appearance, Transform, Interaction, Accessibility, Control, ViewModel, All properties) with color swatches, the XAML source location, and a per-property filter.
- **Layout Explorer** — the selected container's children drawn to scale (with `Grid` cells); click a child to select it.
- **Debug paint (▦ Guides)** — Flutter-style outlines of every visible element, color-cycled by depth.
- **Measure distances (↔)** — pick a second element and get Figma-style gaps or edge offsets (see [badges](#measure-mode-badges)).
- **Mirror (📱)** — live device screenshots in the browser; click the image to select the element under the cursor.
- **Console dump / diff** — the whole tree with bounds, margins, paddings, spacings, sibling gaps, fonts and colors, ready to compare against a Figma design; **Δ Diff** stores a baseline and shows exactly which lines changed.
- **Accessibility** — editable `SemanticProperties` plus a WCAG contrast check against the effective background.
- **Performance (⏱)** — live fps / average / worst frame time; **🐢 Slow** runs all animations 5× slower.
- **Memory (🧠)** — what is still in memory and why: leak snapshots, live readings, heap dumps ([details](#memory--leaks)).

### Measure mode badges

After enabling `↔` and picking a second element, distance labels appear on the overlay:

| Badge | Meaning |
| --- | --- |
| `W × H` (dark) | Size of the **primary** (first selected) element — not a distance. |
| `←n→` | Free **horizontal gap** between the two elements (outer spacing). |
| `↑n↓` | Free **vertical gap** between the two elements (outer spacing). |
| `L n` | Offset between the **left** edges of primary and compare. |
| `R n` | Offset between the **right** edges. |
| `T n` | Offset between the **top** edges. |
| `B n` | Offset between the **bottom** edges. |

**When which ones show**

- Side-by-side (no X overlap): `←n→` plus `T` / `B` if those edges are not aligned.
- Stacked (no Y overlap): `↑n↓` plus `L` / `R` if those edges are not aligned.
- Diagonal (no overlap on either axis): `←n→` and `↑n↓`.
- Nested / intersecting on both axes: `L` / `R` / `T` / `B` (no outer gap).

Values are in **dp**. Aligned edges (delta ≈ 0) are omitted.

## Editing properties

- **Live editing** — text/number fields, switches and pickers for anything with a public setter: `FontSize`, `Margin`, `Padding`, `Text`, colors (`#RRGGBB`, `#AARRGGBB` or named), `Thickness` (`8`, `8,4`, `8,4,8,4`), enums, `LayoutOptions`, `Keyboard`, `Image.Source`… The highlight re-measures after every change.
- **Markup extensions** — type `{Binding X}`, `{StaticResource Y}`, `{OnPlatform …}` or your own extension (`{extensions:Translate Key}`) into any editor and it is applied for real; a custom extension that cannot be instantiated is kept as a XAML-only edit instead of landing as literal `{…}` text.
- **Suggestions** — text editors offer what actually fits the property: registered font aliases for `FontFamily`, and `{StaticResource Key}` type-ahead over the resources whose value matches the property type (colors for `TextColor`, doubles for `FontSize`, strings for `Text`…). The **⋔** button opens a small per-platform / per-idiom form (iOS · Android · WinUI, Phone · Tablet · Desktop); the applied expression is shown next to the value and remembered across app restarts.
- **Markup origins** — XAML-authored expressions are resolved at parse time, so at runtime a property only holds the result; the inspector reads the element's tag from the **XAML embedded in the assembly** and shows the truth as badges: `⋔ {OnIdiom Phone='16,0', Tablet='37,0'}` next to the resolved `Margin`, `🖌 {StaticResource EazleFontFamily}` next to the resolved `FontFamily` — spans included. Style setters resolved from a `{StaticResource}` display as that reference too, with the referenced resource editable right underneath in the Resources popup.
- **Binding-aware** — bound properties show a `⛓ {Binding …}` badge (compiled `x:DataType` bindings included — the path is reconstructed from the `TypedBinding`), and literal edits on them stay runtime-only so the binding expression in your XAML is never overwritten by a constant.
- **Styles** — the current `Style` resolved to its resource key with all setters listed, and a picker to apply any other reachable style (local values are cleared so the style actually takes effect). See [Styles & resources](#styles--resources) for style extraction and the editable Resources popup.
- **Shadow** — `＋ Add shadow` with per-part editors, a `Shadow` field that accepts (and suggests) `{StaticResource …}` shadows, and a `🖌 style` badge when the shadow comes from a style. Runtime-created shadows are written to XAML in the converter form (`Shadow="0 4 8 #66000000 0.5"`); XAML-declared `<Shadow>` tags are patched in place.
- **Spans** — a `Label`'s `FormattedText` expands into per-span sections with add/remove, and can be created from the plain `Text`.
- **Grid** — editable row/column definitions (`Auto`, `*`, `2*`, `48`) with add/remove — `{OnIdiom …}`/`{OnPlatform …}` accepted per definition (the ⋔ editor works here too) — plus `Grid.Row/Column/RowSpan/ColumnSpan` on children, and the mirror's grid designer for drag-resizing tracks.
- **ViewModel** — the selected element's `BindingContext` properties, editable for simple types (in-memory only).
- **Edit history** — every applied edit logged old → new, with one-click undo.

### Custom controls are first-class

Selecting one of your own controls adds a **“{Type} properties”** section listing the bindable
properties it declares (one section per type in the inheritance chain), with the same editors,
history, `{Binding}`/`{StaticResource}`/`{OnPlatform}` support and XAML write-back as the
built-in sections. `ImageSource` properties accept a bundled file name or an absolute URL.

## Styles & resources

### Extract style

Right-click → **✂ Extract style…** turns an element's local property values into a keyed
`Style` in the page's resources: a dialog proposes a key (`{Type}Style`) and pre-selects the
style-able values (content-ish ones like `Text` are listed but unchecked). Applying builds the
style, clears the extracted local values, re-points the element at `{StaticResource key}` —
live, with undo — and writes the `<Style>` block into `<Page.Resources>` (creating the section
when missing) with the element's attributes swapped for the resource reference.

### The Resources popup

![Resources popup](docs/resources-popup.png)

The **🎨 Resources** button opens a popup over the panel listing every reachable resource
dictionary — application, merged files, and the presented pages' own dictionaries — with a
search box over keys, styles and setters. Colors and brushes get swatches and are editable;
**scalar resources** (`x:Double`, `x:String`, booleans, `Thickness`, `CornerRadius`) are
editable too; and each `Style` expands into its setters, editable inline (the style re-applies
to its live consumers immediately). Every change is also recorded for the XAML Updater, which
patches the owning dictionary file — located by `x:Key`, no line anchors — or the page file for
inline page resources. `DynamicResource` consumers update live; `StaticResource` references were
resolved at inflation time and show the new value after the page is rebuilt.

### The design cookbook

**📚 Cookbook** — a top-level view of the web panel, **📚 Cookbook** in the `⋯` row of the on-device
panel, or `MauiInspector.ShowCookbook()` — turns the app's design system into a gallery you check
at a glance: after a global style edit, after a theme switch, before a release.

- **Colors** — every `Color` and brush resource as a swatch with its hex and dictionary file.
- **Typography** — every font registered with `ConfigureFonts` as a type specimen (alias · file), plus the keyed `Label` / `Span` styles rendered on sample text.
- **Styles** — every keyed style rendered on an instance of its `TargetType` (`BasedOn` and setter count in the caption); implicit styles for pages and Shell are listed with their setters.
- **Controls** — every toolbox control, built-in and your own, with its **implicit** look — stacked with a **disabled** twin, so the `Disabled` visual state gets checked too.
- **Templates** — `ControlTemplate`s with placeholder content, `DataTemplate`s without data.
- **Images** — every bundled image (`MauiImage`) plus `ImageSource` / `FontImageSource` resources.
- **Scalars & shadows** — `x:Double`, `Thickness` (drawn as insets), `CornerRadius`, `Shadow`s on a card.

The samples render on the device, on a real page pushed modally, so implicit styles,
`AppThemeBinding`s and `DynamicResource`s behave exactly as on any screen of the app — and the
whole inspector works on that page: long-press a tile to inspect it, the tree lists it, the mirror
shows it. One section shows at a time, **20 tiles per page** in a virtualized list (chips and
‹ › on the device, chips in the panel), so a design system with hundreds of controls stays
smooth — nothing accumulates when you switch. **Tap a tile** to open the sample on a page of its
own, laid out at the **full screen width** (or at the width the control declares); **▤** in its
header opens the property sheet underneath — the control's own bindable properties first, the
inherited sections (Layout, Appearance, Text…) folded in an accordion, every value editable live.

Controls that draw on their data context — `{Binding Colors[…]}`, localized texts, a view
model's services — look bare outside the app; give the samples the same context the screens
would: `options.Cookbook.BindingContext = () => new DesignTimeViewModel();`. The backdrop
behind the samples follows the app's implicit page style; when your pages paint their own
background (a transparent page style, a gradient) set `options.Cookbook.LightBackground` /
`DarkBackground` (or `Background` for a brush) so the tiles and the web previews get the real one.

In the web view every tile is a PNG captured on the device — **headlessly**: the samples render
on an off-screen stage that is logically part of the presented page (so its styles, resources and
theme apply), and nothing appears on the device screen. Click a tile (or **⤢ Open**) for a
full-width capture of the sample alone, with **▤ Properties** unfolding its sheet; the gallery page
only shows on the device when you ask for it — **📱 Open on device** in the panel, or **▤ Panel →
📚 Cookbook** on the device — and then the captures come from the tiles on screen and **⌖ Inspect**
selects a sample in the tree (properties, style, write-back). **🎨 Edit** opens the Resources popup on the key,
**⧉** copies `{StaticResource Key}`, and the **state** picker forces any visual state the sample
declares (`PointerOver`, `Focused`…). The theme buttons switch the **whole app** between system / light /
dark (`Application.UserAppTheme`). Tiles re-capture after every edit made from the panel or the device.

**📌 Baseline → Δ changed** is the regression check: click Baseline, edit a global style or a color
resource, and the tiles whose pixels changed get an amber ring — hover shows the *before* image,
**Δ changed only** filters the rest away. What was supposed to change did, and nothing else.

**Recipes** — samples you author yourself. A `DataTemplate` keyed `Cookbook.<Section>.<Name>` in
any resource dictionary becomes a tile with exactly that content, in a section named after the key:

```xml
<DataTemplate x:Key="Cookbook.Buttons.Primary and secondary">
    <HorizontalStackLayout Spacing="8">
        <Button Text="Save changes" Style="{StaticResource PrimaryButton}" />
        <Button Text="Cancel" Style="{StaticResource SecondaryButton}" />
    </HorizontalStackLayout>
</DataTemplate>
```

Real markup, hot-reloadable, editable from the inspector with XAML write-back like any page.
Bindings inside a recipe have no data context — set `BindingContext` in the template (an
`x:Static` design-time object works) when the sample needs data.

Controls are instantiated with their parameterless constructor. Which ones appear is a matter of
two prefix lists — matched against the full type name **or** the control's XAML file path, so a
namespace and a folder work alike:

```csharp
options.Cookbook.IncludedControls.Add("MyApp.Controls.DesignSystem.");   // only the current design system…
options.Cookbook.IncludedControls.Add("Views/New/");                     // …or everything under that folder
options.Cookbook.ExcludedControls.Add("MyApp.Controls.CameraPreview");   // a constructor that starts hardware
```

Move the legacy controls into a namespace (or folder) of their own and list only the new one —
nothing else needs naming. A constructor that throws just shows the exception on its tile.

The other sections answer to `IncludedResources` / `ExcludedResources` the same way — prefixes of
a resource key, of its dictionary file, of an image or font file name, or of a style's target type,
optionally scoped to one section with `section:`:

```csharp
options.Cookbook.IncludedResources.Add("Resources/Styles/DesignSystem/");   // only these dictionaries…
options.Cookbook.IncludedResources.Add("Resources/Images/2.0/");             // …and the images from this folder
options.Cookbook.IncludedResources.Add("typography:Brand");                  // fonts whose alias starts with Brand
options.Cookbook.ExcludedResources.Add("colors:Gray");                       // minus the gray ramp in Colors
options.Cookbook.ExcludedResources.Add("styles:Legacy");                     // keys starting with Legacy, Styles only
options.Cookbook.ExcludedResources.Add("scalars:*");                         // a whole section
```

Bundled images keep only their file name in the app package, so the package ships a build target
that records each `MauiImage`'s source path (`Resources/Images/2.0/beer.svg`) in an embedded
manifest — that is what the folder prefix matches. It is imported automatically with the package
reference; set `<MauiInspectorImageManifest>false</MauiInspectorImageManifest>` to opt out.

## WYSIWYG editor

Properties are half the story — the inspector also edits the **structure** of a running page:
add controls, delete them, reorder, reparent, wrap and unwrap, copy & paste — live on the
device, recorded in the edit history with full undo/redo, and (with the
[XAML Updater](#xaml-updater-sync-tool) running) written back into
your `.xaml` sources as real, compilable markup.

![Structure editing overview](docs/wysiwyg-overview.png)

### The toolbox

Turn on **Mirror** and a toolbox appears next to the live screenshot: every MAUI built-in plus
**your app's own controls**, discovered by reflection (public `View` subclasses with a
parameterless constructor — marked `custom`). Drag a control onto the mirror: while you drag,
the container that would receive the drop is outlined with its type name, and the drop position
follows the cursor (above/below the neighbouring children in stack layouts).

![Drop target highlight](docs/wysiwyg-drop-target.png)

The `⛶` button expands the mirror into a full column — tree, mirror and properties side by
side — and `🗗` docks it back. The **Fit** button, zoom slider (25–300%, or pinch/Ctrl+scroll)
and drag-to-pan keep big tablet screenshots manageable; clicking, right-clicking and dropping
all stay accurate at any zoom, pan and device rotation. The mirror starts automatically when
the panel opens, and on Android it captures the real GPU frame of **every window** (modal pages
live in separate dialogs there), so what you see is exactly what the device shows. Frames are
JPEG-encoded off the UI thread with captures never queueing up behind a slow one, so the
running app stays smooth while the mirror streams.

With **Select mode off**, clicking the mirror forwards the tap into the app itself — like a
remote-desktop client. On Android a real touch event is injected (buttons, list rows, entries —
everything reacts); on the other platforms the tapped element's own handlers are triggered
(`TapGestureRecognizer`, buttons, switches, checkboxes). Flip Select back on and clicks select
elements again. Rotating the device clears the selection — adaptive layouts rebuild on rotation,
so a kept selection would point at a stale element.

### Designer aids

While a toolbox drag is in flight, **snap lines** show how the drop position relates to the
container's children. The selection gets **alignment pins** reflecting its
`HorizontalOptions`/`VerticalOptions`, and selecting a `Grid` overlays a **grid designer**:
drag the row/column lines to resize tracks (written as absolute dp), or use the `+row`/`+col`
buttons on the mirror. The `</>` button above the properties shows a live **XAML preview** of
the selection — the exact markup a copy/paste or write-back would produce.

### The context menu

Right-click a tree row — or right-click **directly on the mirror** (the element under the
cursor is hit-tested and selected) — for the full set of operations:

![Context menu](docs/wysiwyg-context-menu.png)

- **Add element…** opens a searchable catalog with a one-line description of every control.
- **Copy** / **Copy with content (force)** / **Paste here** — see below.
- **Wrap in…** puts the element inside a new container (Grid, Border, ScrollView, …) chosen
  from the same catalog, filtered to containers. The wrapper lands in the XAML around the
  element's markup, indentation included; editing the wrapper's properties rewrites only its
  opening tag.
- **Unwrap** pulls the element one level up: if it was its parent's only child the parent
  container disappears (your `<Grid><VerticalStackLayout/></Grid>` becomes just the stack);
  with siblings present, the element moves out to the grandparent instead.
- **Move up / Move down** reorder within the parent — dragging rows in the tree does the same,
  including dropping into a *different* parent (edges = before/after a sibling, middle = into
  the container).
- **Remove element** — also on the Delete/Backspace key.

![Add element catalog](docs/wysiwyg-catalog.png)

### Copy & paste

`Ctrl/Cmd+C` copies the selected element — its non-default property values and its whole
subtree; `Ctrl/Cmd+V` pastes into the selection (or its nearest container ancestor). The pasted
markup is written to the XAML as a complete nested block, custom controls included: their
`xmlns:` declarations are added to the root element automatically, reusing prefixes the file
already has. Custom controls are treated as *leaves* by default — their internal visual tree
belongs to them and is not duplicated. For wrapper-style controls that carry your content, use
**Copy with content (force)** (`Ctrl/Cmd+Shift+C`).

### History with undo & redo

Every edit — properties and structure alike — lands in the **Edit history**. `Ctrl/Cmd+Z` walks
the chain backwards like a classic editor: undone entries are struck through and leave the
chain, so repeated undo keeps going deeper instead of re-doing itself. `Ctrl/Cmd+Shift+Z` (or
`Ctrl+Y`) re-applies the most recently undone entry; making a new edit clears the redo branch.

![Edit history](docs/wysiwyg-history.png)

### Durability

- With the SQLite storage package, structural edits **survive app restarts**: pending adds
  (with their edited attributes), removes, moves and wraps are re-applied when the page loads,
  matched by XAML source identity — until the XAML Updater has written them into the sources
  and they become plain markup.
- The XAML Updater applies structural operations with the same in-place, no-reformat policy as
  attribute edits: inserts are anchored to their parent and neighbours, later edits *upsert*
  the same snippet instead of duplicating it, moves relocate the element's exact span
  (re-indented for its new depth), and undo restores the removed text verbatim. Structural
  operations are only served to an updater that declares support for them, so an outdated tool
  can never misapply them.

## XAML Updater (sync tool)

The inspector can act as a real WYSIWYG editor: edits made in the web panel (or on the
device) are written back into your XAML source files.

1. In debug builds MAUI records the XAML source location (file + line) of every element —
   `UseMauiInspector` enables this automatically. The panel shows it above the
   properties (e.g. `MainPage.xaml:26:14`). **Requires runtime/XamlC inflation** — remove
   `<MauiXamlInflator>SourceGen</MauiXamlInflator>` from the app project for Debug.
2. Install the companion tool once, then run it from your app's source folder:

   ```bash
   dotnet tool install -g Immons.Tools.Maui.Inspector.Sync   # once
   cd path/to/your/app
   maui-inspector-sync
   ```

   The tool ships as a **.NET global tool** (`net10.0`), so `maui-inspector-sync` is on your
   `PATH` right after installing — on a fresh machine open a new terminal first, and if the
   command is still not found add `~/.dotnet/tools` (Windows: `%USERPROFILE%\.dotnet\tools`)
   to `PATH`. Housekeeping:

   ```bash
   dotnet tool update    -g Immons.Tools.Maui.Inspector.Sync   # newer version
   dotnet tool uninstall -g Immons.Tools.Maui.Inspector.Sync   # remove
   dotnet tool list      -g                                    # what is installed
   maui-inspector-sync --help                                  # all options
   ```

   Prefer not to install it globally? `dotnet tool install --local` (with a
   `dotnet-tools.json` manifest in the repo) works too — then run it as
   `dotnet maui-inspector-sync`, and everyone on the team gets the same version.

   Zero configuration, **all devices at once**: it scans localhost ports 9295–9309 and
   watches **every** inspector it finds — each app keeps its own change cursor, all edits
   land in the same sources — and keeps rescanning, so a simulator started later joins
   automatically. One app reachable through several ports (old + new adb forwards) is
   recognised by its instance id and watched exactly once. `--app` (repeatable or
   comma-separated) pins explicit URLs; `--src` overrides the source folder.

   Android plumbing is automatic and collision-aware: an emulator has its own loopback, so
   the tool probes each connected device through temporary `adb forward`s, finds the ports
   its inspectors actually listen on, and maps each onto a **free** host port from the same
   range — stepping around ports already taken (an iOS simulator app, another forward).
   `maui-inspector-sync forward` runs just this step and prints the URLs, useful when you
   only want mirrors without watching sources.

   The panel header shows `XAML Updater ✓` once the tool is connected. When you open the
   panel with editing off, it offers to enable it — and when no updater is running, it shows
   the exact commands to start one (including the `adb forward` line when needed) and
   verifies the tool is really polling before enabling.

3. Toggle **✎ XAML** in the panel header. From now on every applied edit
   (FontSize, colors, margins, styles, span/shadow attributes, `Grid.Row`, `{Binding …}`,
   `{OnPlatform …}`…) is patched into the right attribute of the right tag — plain-text edits,
   no reformatting. Style extraction inserts the `<Style>` block into the page resources,
   setter and scalar-resource edits patch the owning dictionary file (located by `x:Key`).
   Only the latest value per attribute is written; the toggle can be flipped on/off at any time.
4. Every write is **confirmed back to the panel**: the edited field shows a spinner while
   the updater works, then ✓ when the value landed in the file — or ⚠ with the exact reason
   (file not found under `--src`, drifted anchor…) when it did not, so a silently-lost edit
   cannot happen.
5. Pair it with your IDE's **XAML Hot Reload** and the loop closes: web edit → file save →
   hot reload → app updates.

Safety: the XAML Updater verifies the element name at the recorded location and skips (with a warning)
when the file has drifted — after editing XAML by hand, restart the app to refresh locations.
Edits of objects that don't come from XAML (created in C#) are not recorded.

## Network & HTTP mocking

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
[Packages](#packages) — release builds compile without the inspector at all.

**Logs** — stream `ILogger` output into the panel's Logs view:

```csharp
// register AFTER any ClearProviders()
builder.Logging.AddMauiInspector();
```

### The Network and Mocks views

**Network — requests, breakpoints and bodies**

![Network requests](docs/web-network.png)

Every call that goes through `MauiInspectorHttpHandler` is recorded in an aligned table
(time · status · method · URL · duration · size · matched rule) with full request and
response bodies (click a row to expand). Breakpoints pause matching requests or responses so you
can edit the body or status and continue — Proxyman-style, but inside the process, so TLS and
certificate pinning are none of your concern.

**Mocks — rules, scenarios and recording**

![Mock rules and scenarios](docs/web-mocks.png)

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

## Memory & leaks

The **🧠 Memory** view answers "what is still in memory, and why" for the app the panel is connected to.

![Memory view](docs/web-memory.png)

- **Which process** — the app's name and pid sit above the readings: two apps on one simulator take neighbouring panel ports, and measuring the wrong one looks exactly like a fixed leak.
- **Live readings** — managed heap, GC counts (gen 0 / 1 / 2), bytes allocated so far; on Android also the Java heap, the native heap and the JNI global-reference count (the classic leak signal there); on iOS the physical footprint Xcode's gauge shows; on Windows the working set. A sparkline of the last minutes and a **♻︎ GC** button.
- **📌 Baseline → run → snapshot** — the measurement that settles it. Mark the baseline, walk your flow (push a page, go back, repeat), snapshot: every type shows what it **grew by since the baseline** and **per repetition**. "+9 objects per navigation, exactly linear" is a leak; "394 detached" alone is not a diagnosis.
- **⏻ Tracking** — the whole memory layer's on/off switch, at runtime, from the panel. Off means the inspector records nothing, empties its registry (so it holds no reference to any of the app's objects) and turns watch mode off with it; the per-element hooks return immediately, so the app carries no inspector memory work at all. The live readings — managed heap, process memory, the sparkline, the platform peers — keep working, because they cost one reading per second and nothing per element. Turning it back on re-reads the visual trees, so the tracked table fills up again from what is on screen. `options.Memory.TrackInstances` is the same switch at startup. The readings keep updating with it off, and that is not leftover work: nothing samples inside the app between polls — the panel asks, the app reads the counters on the HTTP thread and answers (~5 ms per call on an Android emulator, against ~13 ms of bare request overhead), never on the UI thread. With tracking off the panel drops to one poll every 5 s, and it stops polling entirely when you leave the Memory tab or put the browser tab in the background.
- **📸 Snapshot** — the leak detector. The inspector keeps a *weak* reference to every element that enters a window, plus its view model (`BindingContext`), handler and platform view — one weak reference per object, nothing else. A snapshot runs several full collections (`options.Memory.CollectionsPerSnapshot`, default 5: MAUI releases handlers and platform peers a round late), then sorts the survivors into **in a window** (fine), **detached** (alive although no window uses it — the suspects) and **collected**. A view model that no element is bound to is not automatically a suspect: if a live screen still reaches it — the filter built on one page and read by the popup that opens next — it counts as in use. Something that came loose seconds ago is listed with a *just detached* marker and sorted below the rest — often it is state between screens — but it is always listed: a leak is fresh the first time you see it too. Collected counts what died since the previous snapshot, so two snapshots in a row show zero — the running total since the baseline is shown next to it, and that is the number to read. With a heap dump at hand, each group also carries **what it costs** — the retained size of those instances, i.e. the bytes that would go away with them — and the header sums it: *"442 detached of 972 alive · holds 8.0 MB"*. Suspects come grouped by type with the MAUI-specific evidence: *page*, *handler still connected* (`DisconnectHandler` never ran), *inside DetailsPage* (a child along for the ride), the view model type, and how many snapshots they have survived — and each group carries a **💡 how to fix** note matched to that evidence (static events and messengers for pages, DI lifetimes for view models, `DisconnectHandlers()` for handlers, the iOS retain cycle for platform views). Click a group for its **parent chain** — the tree the object still sits in, up to the oldest ancestor, which is what the heap dump then has to explain. The table lists every tracked type with live / in-a-window / detached counts and **Δ** against the previous snapshot — push a page, go back, repeat, snapshot: `DetailsPage ×5, detached` is the leak, `+5` after the next round confirms it.
- **👁 Watch mode** — off by default (each snapshot is a few full collections): once on, a snapshot follows navigation in both directions — a page arriving matters as much as one leaving, because state the app parks between screens is in use again the moment the next screen binds it — debounced by `options.Memory.WatchDelay` so a burst of navigation costs one snapshot. The panel picks it up on its own; the suspects refresh without touching 📸 Snapshot. The **navigation ledger** records each page's push, pop, the memory it cost and the verdict — *collected* or *still alive* with the count of snapshots survived. A badge on the Memory tab counts the pages still alive. Toggle from the panel or `options.Memory.WatchNavigation`.
- **Your code vs its packages** — everything the Memory view calls an *app type* comes from your own assemblies: the one your `App` class lives in and its siblings under the same root name (`Contoso.Shop.Mobile` also owns `Contoso.Shop.Model`). Third-party packages — CommunityToolkit, SQLite-net, Mapster — are neither framework nor yours: they get their own colour and are never announced as "the place to look". `options.Memory.AppAssemblyPrefixes` overrides the rule.
- **Screens the inspector cannot see** — the ledger notices every `Page` that enters a window, which is most apps but not all: an overlay host that adds a layer to the current page, a custom modal, a tab shell of your own never pushes a `Page`, so for the ledger nothing happened. Report those and they behave like any pushed page:

  ```csharp
  using Inspector = Immons.Tools.Maui.Inspector;   // MAUI already has a Navigation in every page
  …
  Inspector.Navigation.ReportPushed(layer, "CheckoutOverlay");   // or the layer's view model
  …
  Inspector.Navigation.ReportPopped(layer);
  ```

  Only a weak reference is kept, the entry is marked *reported* in the ledger, and it counts towards the per-repetition growth like everything else.
- **Who holds it, in-process** — every snapshot also scans the static fields of the app's own types and the events and fields of the long-lived objects (`Application`, its windows, the `Shell`, the page containers): a static event with a handler on a popped page, a static list a view model sits in, a `Window` event a page subscribed to — reported on the suspect as *held by static event LeakSource.Tick → OnTick*, with the remedy. Most MAUI leaks end here, without a dump.
- **Parents chain & history** — click a suspect group for the tree it still sits in, up to the oldest ancestor; the **snapshot history** chart plots the detached count per app type across snapshots — the line that keeps climbing is the leak.
- **Bisection aids** — `🧪 disconnect handlers on pop` and `🧪 clear BindingContext on pop`: repeat the flow with one of them on; if the suspects vanish, you know whether the handlers or the view model held the page. Diagnosis only, never a fix.
- **Images** — the decoded bitmaps of the tracked `Image` / `ImageButton` elements with their size and bytes (Android adds every `Bitmap` the runtime still wraps, shown or not) — the usual native-memory hog on phones.
- **OS signals** — iOS memory warnings and Android `onTrimMemory` / `onLowMemory` land as red markers on the sparkline (gen-2 collections as thin ones); iOS shows the **headroom** before jetsam, Android the PSS and graphics memory.
- **Java peers** (Android) — Java.Interop's own list of every surfaced peer grouped by managed type, tracker or not, with GREF counts: the view that catches leaked platform views.
- **🧬 Heap dump** — the whole managed heap. The panel orders it, `maui-inspector-sync` on the desktop carries it out with `dotnet-gcdump` (through `dotnet-dsrouter` for Android and iOS, by PID on Windows), reads the `.gcdump` and posts a report back: every type with object counts and bytes, **Δ** against the previous dump, and — for the snapshot's suspects — the **shortest path to the GC root** (found by walking the reference graph backwards, so it is seven hops, not two hundred): `DetailsPage ← EventHandler ← Style ← ResourceDictionary ← [static vars]`. Click a path to see it as a stack, with the app types marked and each delegate hop explained as the event subscription it is. The types table sorts by any column (Objects, Δ, Bytes, Type, Module), filters by module, and can show only the types whose count changed since the previous dump. A dump takes as long as the heap is big — collecting streams every object out of the app as events, roughly **a minute per million objects** (measured: 358 k objects in seconds, 1.1 M in about two minutes), while reading the file back and building the report takes well under a second. The job card ticks the elapsed time so a long collection does not look stuck. Reports fold: the newest is open, the older ones are one line each (click to unfold), and a new dump takes the spotlight. The inspector's own objects — its trackers, the reports it holds, the mirror's frame — are hidden from the tables, the largest objects and the chains unless you ask for them with `○ inspector's own`, which also says how much they weigh. The `.gcdump` stays on disk (`--dumps` folder, default `<temp>/maui-inspector/heapdumps`) for Visual Studio or PerfView. Click a path to see it as a stack, with the app types marked, each delegate hop explained as the event subscription it is, and DI singletons named. Every chain is tagged by **what kind of root holds it** — `static` (your own static field), `interop` (a strong `GCHandle`: an ObjC or Java peer, which managed code cannot release — the native object has to go away), `handle`, `root` — which is the difference between "unsubscribe this event" and "the UIViewController was never dismissed". Every root entry carries its **retained size** (what would go away with those instances), the report lists the **largest objects** with their chains, and the types table sorts by any column (Objects, Δ, Bytes, Δ bytes, Type, Module), filters by module, marks types **new** since the previous dump, and traces any type to its roots with the 🧬 button on its row — read from the dump already on the desktop, no new collection.
- **⏺ Allocations** — the same hand-off with `dotnet-trace` for ten seconds: which types allocate how much while you scroll — GC pressure by type, per second, with an **app types only** switch and, when a heap dump is at hand, a **Live** column saying how many of that type are alive right now (plus 🧬 to trace them to their roots). *"`Outlet` allocated 14 MB in 10 s and 11 325 of them are alive"* is a complete sentence. On Android and iOS it is Mono's profiler provider reporting every allocation (a heap dump at the start names the types, so the first second is heavier). Mono only does that when the app was **started** with the allocation profiler, so the Diagnostics package switches it on for Debug builds by itself — nothing to configure; it costs the inlined allocation fast path, which a Debug build barely notices next to the interpreter, and Release never gets it. `<MauiInspectorAllocationTracking>false</MauiInspectorAllocationTracking>` turns it off. On Windows it is the sampled `gc-verbose` profile, no setup. (Mono names a type only while dumping the heap, so the tool asks the app to collect near the end of the recording — what stays unnamed is what never lived through a collection.) `dotnet-trace` is installed the same way as `dotnet-gcdump`.

Heap dumps need two things:

1. On the desktop: `maui-inspector-sync` running against the app. It brings `dotnet-gcdump` and `dotnet-dsrouter` along by itself — on the first dump it installs them (or a current enough copy: `--dsrouter` needs gcdump ≥ 9.0.652701) into `~/.maui-inspector/tools`, leaving your global tools untouched; `maui-inspector-sync tools` does that ahead of time, `--no-tool-install` turns it off.
2. On Android and iOS: a diagnostic port in the app — add the **`Immons.Tools.Maui.Inspector.Diagnostics`** package. It is build-only: its targets switch on the runtime's diagnostics component and a diagnostic port for **Debug** builds, and only there; in Release the package warns instead. `<MauiInspectorDiagnostics>false</MauiInspectorDiagnostics>` switches it off. Windows (CoreCLR) needs nothing.

Each app gets **its own port**, derived from its identity (the even numbers 9010–9088 — the Android router takes the one above it), and the XAML Updater starts one `dotnet-dsrouter` per app on its own socket — so two apps can be dumped at the same time on one machine, and neither blocks the other. `<MauiInspectorDiagnosticPort>9005</MauiInspectorDiagnosticPort>` pins a port of your own. (The tools' own `--dsrouter` shorthand hardcodes 9000, which is why the updater drives the router itself; a stale `maui-inspector-sync` from before this change cannot reach the new ports — restart it after updating.)

```xml
<PackageReference Include="Immons.Tools.Maui.Inspector.Diagnostics" Version="0.9.18" Condition="'$(Configuration)' == 'Debug'" />
```

The on-device panel's `⋯` row has **🧠 Mem** — a Memory pane with a snapshot button, watch mode, the suspects and the ledger, for the phone-in-hand case. Tracking is on by default; **⏻ Tracking** in the web panel turns it off at runtime, `options.Memory.TrackInstances = false` at startup.

**What the inspector itself costs** — it is a memory tool, so: the tracker keeps one weak reference per element, view model, handler and platform view (a few kilobytes for a real app); the mirror's fallback frame is dropped ten seconds after the last one is served; heap-dump reports are held gzipped and each job's card says how much (`holding 9 KB`); the network log, the ILogger sink and the memory timeline are fixed-size ring buffers. A dump of the sample with everything exercised attributes ~10 KB to inspector types.

**Diagnosing without the panel** — `POST /api/memory/snapshot` already returns the suspects with `hints`, `parents`, `owner` and `holders` (what the in-process scan found). Once a heap dump exists, every suspect also carries what only a dump can say: `chains` (the shortest paths to a GC root, as type names), `rootKind` (`static`, `interop`, `handle`, …), `retained` and the `dumpJob` it came from. So a script is two calls:

```bash
curl -s -X POST localhost:9295/api/memory/heapdump -d '{}' > /dev/null      # dumps and waits
curl -s -X POST localhost:9295/api/memory/snapshot | jq '.snapshot.suspects[] | select(.app)
   | {type, survived, rootKind, retained, holders, chain: .chains[0]}'
```

An empty `holders` with `rootKind: "interop"` is an answer, not a gap: the object is held by a native peer (an `NSNotificationCenter` observer, a `UIViewController` that was never dismissed, an Android listener), which no managed scan can see and no managed change can release.

**Leak gate for UI tests** — `MauiInspector.TakeMemorySnapshotAsync()` returns a `MemoryReport` whose `Leaks` lists the app's own types still alive without a window (with what holds them); `options.Memory.OnLeak` fires with the same list after any snapshot that finds new ones. Over HTTP, a Maestro or Appium run does the same with `POST /api/memory/snapshot` and asserts `totals.detached == 0` — or, with watch mode on, `GET /api/memory/ledger` and asserts no entry is `alive`. Exports: **⤓ md** (snapshot, ledger, latest dump as Markdown for a ticket) and **⤓ csv** (the types table).

## Multi-device

- **🖧 Devices** — scan localhost (or add `host:port`, single ports `9500`, lists `9500,9600` or ranges `9400-9420`) to find other instances of the same app, then every property edit, structural action and mock-rule change is mirrored to the checked targets.
- **Device picker** — the header dropdown re-points the whole panel (tree, properties, mirror, resources) at another running app, so the phone layout can be inspected from the tablet's portal without a second tab.
- Targets are addressed by **XAML source identity**, not by element ids — one edit reaches every device rendering that line, including every instance of a `DataTemplate`.
- When a device renders a **different template or a different page variant** (an `AdaptiveTemplateView`-style control, `OnIdiom` layouts, or whole pages picked per form factor such as `Main_iPhone_Page` / `Main_iPad_Page`), that source line does not exist there, so the edit falls back to an identifier of the same type: **`AutomationId` first** (it exists to identify one element), then `StyleId` — which is also what MAUI fills from `x:Name`. The fallback is confined to the **counterpart page**: page type names are normalised by stripping form-factor tokens, so `Main_iPad_Page`, `Main_Android_Tablet_Page` and `MainPage` all count as `Main` and a same-named element on an unrelated screen is never touched.
- `StyleId` is a **weak key** — it doubles as the MAUI CSS `#id` selector and nothing keeps it unique, so two unrelated controls can share one. Several matches are therefore accepted only when they all come from the **same XAML line** (the rows of one `DataTemplate`, which is exactly what fan-out should hit); matches from different lines are a name collision and are refused rather than guessed. No match, or an ambiguous one, is reported as `—`.
- **⧉ all instances of this template** (next to the source path) re-applies the edit locally through the same matcher, so all rows of a `DataTemplate` update at once, not just the selected one.

## UI tests (Maestro, Appium)

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

## HTTP API

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

## Reference

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
| `SeedRulesAsset` | `null` | Rule set (a panel export added as `MauiAsset`) imported when the app starts with no rules — see [UI tests](#ui-tests-maestro-appium). |
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
