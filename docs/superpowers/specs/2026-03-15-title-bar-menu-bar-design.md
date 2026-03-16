# Title Bar and Menu Bar Design

**Date:** 2026-03-15
**Status:** Approved

## Overview

Add a dynamic window title and a full menu bar to the Avalonia Desktop client (`Desktop/MainWindow`), matching the structure of the Windows WinForms version. All menu items are implemented as stubs — empty click handlers — so the visual structure is complete and each item can be wired to real functionality independently in future work.

## Goals

- Window title reflects game name, character name, and connection state dynamically
- Menu bar contains all top-level menus and sub-items from the Windows version
- Checkable items track state with a `bool` field; visual checkmark deferred to feature implementation
- No new files or classes; all changes confined to `MainWindow.axaml` and `MainWindow.axaml.cs`

## Out of Scope

- Functional implementation of any menu item
- Checkmark rendering for checkable items
- Dynamic population of the Windows menu (left empty; populated when sub-windows are implemented)
- NativeMenu (macOS native menu bar) — standard Avalonia `Menu` control only

---

## Window Title

### Format

```
{gamename}: {charactername} [Connected] - Genie {version}
{gamename}: {charactername} [Not connected] - Genie {version}
```

- `gamename` — `_globals.VariableList["gamename"]` (e.g. `"DR"`)
- `charactername` — `_globals.VariableList["charactername"]` (e.g. `"Olinia"`)
- `version` — `Assembly.GetExecutingAssembly().GetName().Version.ToString()`
- When `gamename` is empty, the `{gamename}: ` prefix is omitted
- When `charactername` is empty, it is omitted

### Update Triggers

`UpdateWindowTitle()` is called from:
- `EventConnected` handler (already subscribed in `MainWindow.axaml.cs`)
- `EventDisconnected` handler (already subscribed)

Must be dispatched to the UI thread: `Dispatcher.UIThread.Post(() => this.Title = strTitle)`.

---

## Menu Bar

### Placement

A `Menu` control is added as the first child of the root `DockPanel` in `MainWindow.axaml`, docked to the top (`DockPanel.Dock="Top"`), above the existing connect bar.

### Approach

Pure XAML (Option A). All items declared in `MainWindow.axaml`. Click handlers are `void` stubs in `MainWindow.axaml.cs`.

### Full Item Hierarchy

```
File
  Connect...
  Connect Using Profile...
  ── separator ──
  Open Directory >
    Genie
    Scripts
    Maps
    Plugins
    Logs
    Art
  ── separator ──
  Auto Log                        [checkable]
  Open Log In Editor
  ── separator ──
  Auto Reconnect                  [checkable]
  Classic Connect Window
  Ignores/Gags Enabled            [checkable]
  Triggers Enabled                [checkable]
  Plugins Enabled                 [checkable]
  AutoMapper Enabled              [checkable]
  Images Enabled                  [checkable]
  Mute Sounds                     [checkable]
  ── separator ──
  Show Raw Data                   [checkable]
  Performance Test Parse
  ── separator ──
  Exit

Edit
  Paste Multi Line
  ── separator ──
  Configuration...
  Update Images

Profile
  Load Profile...
  Save Profile
  ── separator ──
  Include Password In Profile     [checkable]

Layout
  Load Layout...
  Load Default Layout
  ── separator ──
  Save Layout As...
  Save Default Layout
  Save Sized Default Layout
  ── separator ──
  Basic Layout
  Icon Bar >
    Dock Top
    Dock Bottom
  Script Bar >
    Dock Top
    Dock Bottom
  Health Bar >
    Dock Top
    Dock Bottom
  Magic Panels
  Status Bar
  ── separator ──
  Align Input to Game Window
  Always On Top                   [checkable]

Windows
  (empty — populated dynamically in a future sub-window feature)

Script
  Script Explorer...
  Update Scripts
  Update Scripts With Maps
  ── separator ──
  Show Active Scripts
  Trace Active Scripts
  ── separator ──
  Pause All Scripts
  Resume All Scripts
  ── separator ──
  Abort All Scripts
  Script Settings

AutoMapper
  Show Window
  Update Maps
  Script Settings

Plugins
  No plugins loaded               [disabled, x:Name="_MenuPluginsNoPlugins"]
  Update Plugins

Help
  Check For Updates
  Force Update
  Load Test Client
  ── separator ──
  AutoUpdate                      [checkable]
  AutoUpdate Lamp                 [checkable]
  Check Updates on Startup        [checkable]
  ── separator ──
  Latest Release Page
  ── separator ──
  Discord
  GitHub
  Wiki
  ── separator ──
  Community Links >
    Play.net
    Elanthipedia
    DR Service
    Lich Discord
    Isharon's Genie Settings
```

---

## Code-Behind Stubs

### `UpdateWindowTitle()`

```csharp
private void UpdateWindowTitle()
{
    var sb = new System.Text.StringBuilder();
    if (_globals.VariableList.ContainsKey("gamename") &&
        _globals.VariableList["gamename"]?.ToString() is { Length: > 0 } gameName)
        sb.Append(gameName).Append(": ");
    if (_globals.VariableList.ContainsKey("charactername") &&
        _globals.VariableList["charactername"]?.ToString() is { Length: > 0 } charName)
        sb.Append(charName).Append(' ');
    sb.Append(_game.IsConnected ? "[Connected]" : "[Not connected]");
    sb.Append(" - Genie ");
    sb.Append(Assembly.GetExecutingAssembly().GetName().Version);
    Dispatcher.UIThread.Post(() => Title = sb.ToString());
}
```

Called from both `EventConnected` and `EventDisconnected` handlers.

### Checkable State Fields

One `bool` field per checkable item, initialized to match the Windows defaults where known:

```csharp
private bool _autoLog = false;
private bool _autoReconnect = false;
private bool _ignoresEnabled = true;
private bool _triggersEnabled = true;
private bool _pluginsEnabled = true;
private bool _autoMapperEnabled = true;
private bool _imagesEnabled = true;
private bool _muteSounds = false;
private bool _showRawData = false;
private bool _alwaysOnTop = false;
private bool _includePasswordInProfile = false;
private bool _autoUpdate = true;
private bool _autoUpdateLamp = false;
private bool _checkUpdatesOnStartup = true;
```

### Stub Handler Pattern

```csharp
private void MenuFile_Connect(object? sender, RoutedEventArgs e)
{
    // TODO: open connect dialog
}
```

All other handlers follow the same pattern. Checkable handlers additionally toggle the field:

```csharp
private void MenuFile_AutoLog(object? sender, RoutedEventArgs e)
{
    _autoLog = !_autoLog;
    // TODO: start/stop logging
}
```

### Initialization

In the constructor (after `InitializeComponent()`):

```csharp
_menuPluginsNoPlugins.IsEnabled = false;
UpdateWindowTitle();
```

---

## Files Changed

| File | Change |
|------|--------|
| `Desktop/MainWindow.axaml` | Add `Menu` docked to top of `DockPanel`; full item hierarchy in XAML |
| `Desktop/MainWindow.axaml.cs` | Add `UpdateWindowTitle()`, checkable fields, and all stub click handlers |

No new files. No new classes.
