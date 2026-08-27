# Getting started

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
