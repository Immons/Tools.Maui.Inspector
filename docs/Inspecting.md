# Inspecting

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
- **Memory (🧠)** — what is still in memory and why: leak snapshots, live readings, heap dumps ([details](Memory-and-leaks.md#memory--leaks)).

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
- **Styles** — the current `Style` resolved to its resource key with all setters listed, and a picker to apply any other reachable style (local values are cleared so the style actually takes effect). See [Styles & resources](Styles-and-resources.md#styles--resources) for style extraction and the editable Resources popup.
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
