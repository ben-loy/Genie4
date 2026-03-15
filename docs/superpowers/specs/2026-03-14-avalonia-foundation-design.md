---
name: Avalonia Foundation — Sub-project 1 of N
description: Design spec for adding a cross-platform Avalonia desktop project to Genie4, replacing the broken Mono/WinForms Mac runtime with pure dotnet.
type: project
---

# Avalonia Foundation Design

**Date:** 2026-03-14
**Branch:** `install-mono-for-mac-linux`
**Sub-project:** 1 of N (Foundation only)

## Background

Mono 6.12's WinForms Carbon backend crashes on 64-bit macOS (`CGDisplayBounds` segfault in SkyLight). The Carbon driver was never ported to 64-bit and is unmaintained. The `net48`/Mono path remains for Linux CI validation but is no longer the recommended Mac runtime.

This sub-project establishes a new `Genie4.Desktop` Avalonia project that builds and runs on Mac (and Linux) using pure dotnet — no Mono dependency for running.

## Scope

**This sub-project covers:**
- New `Genie4.Desktop.csproj` targeting `net8.0`
- Avalonia 11 project scaffolding (App, MainWindow)
- DI host wiring using `Microsoft.Extensions.Hosting`
- Shared core file inclusion (Core, Lists, Script, Utility)
- Placeholder MainWindow: connect bar, text output, command input
- Solution file updated to include new project

**Deferred to future sub-projects (not dropped):**
- Text colors and formatting in output area
- Config panels (Aliases, Triggers, Highlights, Macros, Classes, Subs, Ignores, Names, Variables, Windows, Settings)
- All dialog forms (Profile connect, Reconnect, Edit, Key, ScriptName, Changelog, Download, Exception)
- Mapper and auto-mapper
- Plugin system
- Script explorer
- Custom window skin/chrome (FormSkin)
- Sound, TTS, window flash (platform stubs already in place from this branch)

## Repository Layout

```
Genie4.sln                         (updated — adds Desktop project)
Genie4.csproj                      (unchanged — Windows net6.0-windows + net48)
Desktop/
  Genie4.Desktop.csproj            (net8.0, Avalonia 11)
  Program.cs                       (Avalonia AppBuilder entry point + DI host)
  App.axaml                        (Avalonia Application declaration)
  App.axaml.cs                     (OnFrameworkInitializationCompleted — wires DI)
  MainWindow.axaml                 (placeholder window layout)
  MainWindow.axaml.cs              (code-behind — subscribes to Game events)
```

No files move from their existing locations. The Desktop project includes shared source via `<Compile Include="../...">` relative paths.

## NuGet Packages — Genie4.Desktop.csproj

| Package | Version | Purpose |
|---------|---------|---------|
| `Avalonia` | 11.3.x | Core framework |
| `Avalonia.Desktop` | 11.3.x | Desktop platform backends (Mac, Linux, Windows) |
| `Avalonia.Themes.Fluent` | 11.3.x | Default theme |
| `System.Drawing.Common` | latest | Provides `System.Drawing.Color` cross-platform (used by `Game.cs`) |
| `Microsoft.VisualBasic` | 10.3.0 | Used in `Game.cs` and `Command.cs` |
| `Microsoft.Extensions.Hosting` | 6.0.0 | DI host (matches existing project) |
| `System.Resources.Extensions` | 8.0.0 | Resource loading compatibility |

DLL references via relative `HintPath`:
- `../Libs/Antlr3.Runtime.dll`
- `../Libs/Interfaces.dll`
- `../Libs/Jint.dll`

## Shared File Inclusion

Files included via `<Compile Include="../...">` — compiled in both `Genie4.csproj` and `Genie4.Desktop.csproj`:

**Core:**
- `Core/Command.cs`, `Connection.cs`, `Game.cs`, `GenieError.cs`

**Lists:**
- `Lists/ArrayList.cs`, `Classes.cs`, `CommandQueue.cs`, `Config.cs`, `Globals.cs`, `Highlights.cs`, `Macros.cs`, `Names.cs`, `SortedList.cs`

**Script:**
- `Script/Eval.cs`, `JavaScript.cs`, `LUAScript.cs`, `MathEval.cs`, `Script.cs`

**Utility (all except `EnumWindows.cs`):**
- `Utility/ColorCode.cs`, `Crypto.cs`, `DownloadResult.cs`, `EmbeddedAssembly.cs`, `FileHandler.cs`, `INaturalComparer.cs`, `KeyCode.cs`, `Log.cs`, `PluginServices.cs`, `Sound.cs`, `Updater.cs`, `Utility.cs`, `Win32Utility.cs`, `WindowFlash.cs`, `XMLConfig.cs`

**Excluded:**
- `Utility/EnumWindows.cs` — Windows-only P/Invoke with no `#if` guard
- `Core/LegacyPluginHost.cs`, `Core/PluginHost.cs` — WinForms-dependent plugin hosts
- All of `Forms/`, `Mapper/`, `Plugin/` — UI layer, deferred

`Sound.cs`, `Win32Utility.cs`, and `WindowFlash.cs` compile cleanly on `net8.0` — their platform-specific internals are already wrapped in `#if WINDOWS` guards added earlier on this branch.

## DI / Startup Wiring

```csharp
// Desktop/Program.cs
[STAThread]
static void Main(string[] args) =>
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
```

```csharp
// Desktop/App.axaml.cs
public class App : Application
{
    IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddSingleton<Game>()
            .AddSingleton<MainWindow>();
        _host = builder.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += async (_, _) => await _host.StopAsync();
            await _host.StartAsync();
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted(); // must be last
    }
}
```

## MainWindow (Placeholder)

Layout — code-behind pattern, no MVVM:

```
┌─────────────────────────────────────────────────────┐
│ Account: [__________] Password: [__________]        │
│ Character: [__________]                  [Connect]  │
├─────────────────────────────────────────────────────┤
│                                                     │
│  ScrollViewer > SelectableTextBlock (output)        │
│  (monospace font, appended from Game.EventPrintText)│
│                                                     │
├─────────────────────────────────────────────────────┤
│ Command input: [_______________________________] ↵  │
└─────────────────────────────────────────────────────┘
```

- **Host/port:** `const` values in `MainWindow.axaml.cs`, matching defaults from existing `Connection.cs`
- **Password field:** `PasswordChar='•'` to mask input
- **Output:** Plain text only — colors deferred to the text display sub-project
- **Command input:** Enter key sends text to `Game`'s send method
- **Connect button:** Passes account, password, character, and hard-coded host/port to `Game`'s connection logic

## Success Criteria

This sub-project is complete when:
1. `dotnet build Desktop/Genie4.Desktop.csproj` succeeds on Mac with 0 errors
2. `dotnet run --project Desktop/Genie4.Desktop.csproj` opens the Avalonia window on Mac
3. Entering credentials and clicking Connect successfully authenticates with the Simutronics game server
4. Game text appears in the output area
5. Commands typed in the input bar are sent to the server
6. The existing Windows build (`dotnet build Genie4.csproj -f net6.0-windows`) is unaffected
