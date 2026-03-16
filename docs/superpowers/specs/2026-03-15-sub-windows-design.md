# Sub-Windows Design

**Date:** 2026-03-15
**Status:** Approved

## Overview

Add dockable, floatable sub-windows to the Avalonia Desktop client, matching the multi-window layout of the Windows WinForms version. Each game output channel (`WindowTarget`) gets its own named panel. Panels can be docked inside the main window (arranged in a resizable grid) or torn off as standalone floating OS windows. Layout is persisted across sessions.

## Goals

- All `WindowTarget` values (Main, Room, Thoughts, Combat, Inv, Familiar, Logons, Death, Log, ActiveSpells, Raw, Debug, Other) have their own named panel
- Panels can be docked (inside main window, resizable via `GridSplitter`) or floating (standalone Avalonia `Window`)
- Floating windows can be re-docked
- Panels can be shown/hidden via the Windows menu (checkable items, one per window type)
- Layout (docked panel sizes, floating window screen bounds) is saved and restored across sessions
- Inspect existing Windows layout persistence code before implementing; reuse format or logic where possible

## Out of Scope

- Drag-to-dock (snapping panels by dragging their title bars into drop zones) — docking is done via the "Dock" button; layout is arranged by resizing splitters
- Tab groups (multiple panels sharing one tab strip)
- Per-window font/color configuration (separate future feature)
- Status bar, health bars, icon bar (separate future features)

---

## Component Architecture

### `GameOutputPanel` (UserControl)

A reusable text output panel. Used both when docked and when floating.

**AXAML structure:**
```
DockPanel
  ├── Title bar (DockPanel.Dock="Top")
  │     ├── Label: window name (e.g. "Thoughts")
  │     ├── Button: "Float" (hidden when already floating)
  │     └── Button: "×" (close/hide)
  └── ScrollViewer
        └── SelectableTextBlock (monospace, wrapping enabled)
```

**API:**
```csharp
public partial class GameOutputPanel : UserControl
{
    public string WindowName { get; }          // e.g. "Thoughts"
    public bool IsVisible { get; set; }
    public event EventHandler? FloatRequested;
    public event EventHandler? CloseRequested;

    public void AppendText(string text, Color fg, Color bg);
}
```

`AppendText` contains the inline-building logic currently in `MainWindow.AppendOutput` (normalize line endings → split on `\n` → build `Run`/`LineBreak` inlines → `Inlines.AddRange` → scroll to bottom).

### `FloatingWindow` (Window)

A thin Avalonia `Window` that hosts a single `GameOutputPanel` when detached from the dock.

```csharp
public class FloatingWindow : Window
{
    public FloatingWindow(GameOutputPanel panel);
    public event EventHandler? DockRequested;  // fires when user clicks "Dock" in title bar
}
```

- Hides the panel's "Float" button; shows a "Dock" button in the `FloatingWindow` chrome instead
- On close: hides the window and marks the panel hidden (does not destroy)
- Saves its `Position` and `ClientSize` to layout on `PositionChanged` / `SizeChanged`

### `DockManager`

Central coordinator. Owns all `GameOutputPanel` instances and manages the docked `Grid`.

```csharp
public class DockManager
{
    public DockManager(Grid dockGrid, MenuItem windowsMenu);

    // Called by MainWindow.OnPrintText
    public void Route(Game.WindowTarget target, string targetName,
                      string text, Color fg, Color bg);

    // Called by MainWindow layout menu handlers
    public void SaveLayout(string path);
    public void LoadLayout(string path);
    public void SaveDefaultLayout();
    public void LoadDefaultLayout();

    // Internal
    private void AddToDock(GameOutputPanel panel);
    private void Float(GameOutputPanel panel);
    private void Dock(GameOutputPanel panel);
    private void SetVisible(GameOutputPanel panel, bool visible);
    private void PopulateWindowsMenu();
}
```

**Panel lifecycle:**
1. On first `Route()` call for a window target, create `GameOutputPanel` for it, add it to the dock (rightmost column), add its menu item to the Windows menu (checked)
2. Subsequent `Route()` calls find the existing panel and call `AppendText`
3. If panel is hidden, text is silently discarded

**Windows menu:**
- `DockManager` holds a reference to the `_Windows` `MenuItem`
- On panel creation, inserts a `MenuItem` with `Header = panel.WindowName`, `IsCheckable = true`, `IsChecked = true`
- On show/hide, updates `IsChecked`
- `Main` panel's menu item is always checked and disabled (cannot be hidden)

### `WindowLayoutState`

Plain serializable record (no external dependencies — use `System.Text.Json`):

```csharp
public record PanelLayoutEntry(
    string WindowName,    // e.g. "thoughts", "room"
    bool Visible,
    bool IsFloating,
    double SizeRatio,     // star-size proportion in dock grid (if docked)
    double FloatX,        // screen position (if floating)
    double FloatY,
    double FloatWidth,
    double FloatHeight
);

public record WindowLayoutState(List<PanelLayoutEntry> Panels);
```

---

## Docked Layout Model

The dock area is a single `Grid` inside `MainWindow`. No named zones.

**Column arrangement:**
- `Main` panel always occupies the leftmost `ColumnDefinition` with `Width="*"` (fills remaining space)
- Each additional docked panel gets its own `ColumnDefinition` with a saved star-size (default `200`)
- A `GridSplitter` `ColumnDefinition` (width `4`) is inserted between each panel column

**Adding a panel:**
```
Before: [Main(*) | Splitter(4)]
After:  [Main(*) | Splitter(4) | NewPanel(200) | Splitter(4)]
```

**Removing a docked panel** (hide or float): its column and adjacent splitter are collapsed (`Width = 0`, `Visibility = Collapsed`).

**Re-docking a floating panel**: its column is restored and the splitter made visible again.

**Size persistence**: on `GridSplitter` drag-complete, record each column's `ActualWidth` ratio relative to total width.

---

## Floating Windows

- "Float" button on `GameOutputPanel` title bar fires `FloatRequested`
- `DockManager` handles `FloatRequested`: collapses panel's dock column, creates `FloatingWindow(panel)`, shows it at last known position (or a default offset from main window if first time)
- `FloatingWindow` shows a "Dock" button; clicking fires `DockRequested`
- `DockManager` handles `DockRequested`: closes `FloatingWindow`, restores panel's dock column
- `FloatingWindow.Closed` (user clicks OS close button): panel is marked hidden, menu item unchecked

---

## Text Routing

`MainWindow.OnPrintText` changes from:
```csharp
Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text, color, bgcolor));
```
to:
```csharp
Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    _dockManager.Route(targetwindow, targetwindowstring, text, color, bgcolor));
```

`DockManager.Route` maps `WindowTarget` to panel name:

| WindowTarget   | Panel name     |
|---------------|----------------|
| Main / Unknown | "main"        |
| Combat        | "combat"       |
| Portrait      | "portrait"     |
| Inv           | "inv"          |
| Familiar      | "familiar"     |
| Thoughts      | "thoughts"     |
| Logons        | "logons"       |
| Death         | "death"        |
| Room          | "room"         |
| Log           | "log"          |
| Raw           | "raw"          |
| Debug         | "debug"        |
| ActiveSpells  | "percwindow"   |
| Other         | `targetName.ToLower()` |

`Main`/`Unknown` always route to the Main panel (never create a new one for `Unknown`).

---

## Layout Persistence

**File location:** `{GenieDataDir}/layout.json`

**Before implementing**, read the Windows version's layout save/load code to determine:
- Whether the JSON schema can be shared
- Whether the file location matches

**Save triggers:**
- Main window closing (`Window.Closing` event)
- Panel floated, docked, shown, or hidden
- `GridSplitter` drag completed (`DragCompleted` event)
- `MenuLayout_SaveDefault` / `MenuLayout_SaveAs` stub handlers

**Load:** `DockManager` constructor (or `MainWindow` constructor after `InitializeComponent`) calls `LoadDefaultLayout()`. If file missing or invalid, apply default: Main panel visible docked, all others hidden.

**Default layout** (no saved file):
```json
{
  "Panels": [
    { "WindowName": "main", "Visible": true, "IsFloating": false, "SizeRatio": 1.0, ... }
  ]
}
```

---

## MainWindow Changes

**`MainWindow.axaml`:**
- Replace the `ScrollViewer`/`SelectableTextBlock` output area with a `Grid` named `DockGrid`
- `DockGrid` starts with one `ColumnDefinition Width="*"` for the Main panel (added programmatically by `DockManager`)

**`MainWindow.axaml.cs`:**
- Add `private DockManager _dockManager;`
- Constructor: `_dockManager = new DockManager(DockGrid, _WindowsMenu);` (after `InitializeComponent`)
- `OnPrintText`: delegate to `_dockManager.Route(...)` instead of `AppendOutput`
- Remove `AppendOutput` method (moved to `GameOutputPanel`)
- Wire `MenuLayout_SaveDefault`, `MenuLayout_SaveAs`, `MenuLayout_LoadDefault`, `MenuLayout_Load` to `_dockManager` methods
- Add `_WindowsMenu` field pointing to the `Windows` top-level `MenuItem` (accessed via `x:Name` in AXAML)

---

## Files Changed

| File | Change |
|------|--------|
| `Desktop/GameOutputPanel.axaml` | New UserControl — title bar + scroll + text block |
| `Desktop/GameOutputPanel.axaml.cs` | New code-behind — `AppendText`, float/close events |
| `Desktop/FloatingWindow.axaml` | New Window — thin host for floating `GameOutputPanel` |
| `Desktop/FloatingWindow.axaml.cs` | New code-behind — dock button, position/size tracking |
| `Desktop/DockManager.cs` | New class — routing, grid management, menu population, layout I/O |
| `Desktop/WindowLayoutState.cs` | New record — layout serialization model |
| `Desktop/MainWindow.axaml` | Replace output area with `DockGrid`; name `Windows` menu item |
| `Desktop/MainWindow.axaml.cs` | Add `DockManager`, route `OnPrintText`, wire layout menu stubs |
