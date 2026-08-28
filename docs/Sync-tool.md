# Sync tool (maui-inspector-sync)

`maui-inspector-sync` is the desktop half of the inspector — one command in your app's source
folder, and it does the work that cannot be done from inside the app:

- **writes edits back into your XAML sources**, so the panel is a real WYSIWYG editor rather than a
  runtime-only playground (the rest of this page);
- **runs the heap dumps and allocation recordings** the Memory view offers, driving `dotnet-gcdump`,
  `dotnet-trace` and `dotnet-dsrouter` and reporting types, sizes and root paths back to the panel
  ([Memory & leaks](Memory-and-leaks.md));
- **finds your devices and forwards their ports**, so an app on an Android emulator shows up in the
  browser without a single `adb forward` typed by hand;
- **watches several apps at once**, keeping one source folder in sync with every simulator,
  emulator and phone it can reach.

Everything below is the XAML write-back loop.

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

   The panel header shows `Sync tool ✓` once the tool is connected. When you open the
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

Safety: the sync tool verifies the element name at the recorded location and skips (with a warning)
when the file has drifted — after editing XAML by hand, restart the app to refresh locations.
Edits of objects that don't come from XAML (created in C#) are not recorded.
