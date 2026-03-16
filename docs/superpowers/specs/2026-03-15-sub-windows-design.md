# Sub-Windows Design

**Date:** 2026-03-15
**Status:** Approved

## Overview

Add dockable, floatable sub-windows to the Avalonia Desktop client, matching the multi-window layout of the Windows WinForms version. Each game output channel (`WindowTarget`) gets its own named panel. Panels can be docked inside the main window (arranged in a resizable grid) or torn off as standalone floating OS windows. Layout is persisted across sessions.

## Goals

- All `WindowTarget` values (Unknown, Main, Room, Thoughts, Combat, Inv, Familiar, Logons, Death, Log, ActiveSpells, Raw, Debug, Portrait, Other) have routing support; `Unknown` routes silently to the Main panel (no dedicated panel created for it)
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
- `MenuLayout_SaveSizedDefault`, `MenuLayout_Basic`, `MenuLayout_MagicPanels` — leave as no-op stubs (out of scope)

---

## Component Architecture

### `GameOutputPanel` (UserControl)

A reusable text output panel. Used both when docked and when floating.

**AXAML structure:**
```
DockPanel
  ├── Title bar (DockPanel.Dock="Top")
  │     ├── Label: window name (e.g. "Thoughts")
  │     ├── Button: "Float" (Visibility bound to IsFloating — Collapsed when IsFloating=true)
  │     ├── Button: "Dock"  (Visibility bound to IsFloating — Collapsed when IsFloating=false)
  │     └── Button: "×" (close/hide)
  └── ScrollViewer (x:Name="OutputScroll")
        └── SelectableTextBlock (x:Name="OutputText", monospace, wrapping enabled)
```

**Constructor:** `public GameOutputPanel(string windowName)`

**API:**
```csharp
public partial class GameOutputPanel : UserControl
{
    public GameOutputPanel(string windowName);

    public string WindowName { get; }           // e.g. "thoughts"; lowercase; set in constructor
    public bool IsFloating { get; set; }        // true when hosted in FloatingWindow; toggles Float/Dock button visibility
    public bool IsOutputHidden { get; set; }    // true when panel is user-hidden; AppendText discards text when true
    public ScrollViewer OutputScroll { get; }   // exposes inner scroll viewer (used by DockManager for MainScrollViewer property)

    public event EventHandler? FloatRequested;  // fired by Float button click
    public event EventHandler? DockRequested;   // fired by Dock button click
    public event EventHandler? CloseRequested;  // fired by × button click

    public void AppendText(string text, Color fg, Color bg);
}
```

`AppendText` contains the inline-building logic currently in `MainWindow.AppendOutput` (normalize line endings → split on `\n` → build `Run`/`LineBreak` inlines → `Inlines.AddRange` → scroll to bottom). Text is only appended when `IsOutputHidden == false`.

### `FloatingWindow` (Window)

A thin Avalonia `Window` that hosts a single `GameOutputPanel` when detached from the dock.

```csharp
public class FloatingWindow : Window
{
    // dockAction:  called when user clicks Dock — typically () => dockManager.Dock(panel)
    // hideAction:  called when window is hidden/closed — typically () => dockManager.OnPanelHiddenWhileFloating(panel)
    public FloatingWindow(GameOutputPanel panel, Action dockAction, Action hideAction);
}
```

**Construction:** Sets `panel.IsFloating = true`. Assigns `Content = panel` (no AXAML body needed; `FloatingWindow.axaml` is a minimal `<Window>` with no child elements). Subscribes to `panel.DockRequested`, `panel.CloseRequested`, and `Window.Closing`.

**Dock sequence (unambiguous ordering):**
1. User clicks Dock button → `panel.DockRequested` fires
2. `FloatingWindow` sets an internal `_suppressHide = true` flag
3. `FloatingWindow` calls `dockAction()` — this runs `DockManager.Dock(panel)` synchronously, restoring the docked column
4. `FloatingWindow` calls `this.Close()` — the `Window.Closing` handler sees `_suppressHide = true` and skips the hide logic
5. `DockManager.Dock` does NOT call `FloatingWindow.Close()` — `FloatingWindow` closes itself after `dockAction()` returns

**Close/hide sequence (OS close button or "×"):**
1. `Window.Closing` fires (or `panel.CloseRequested`)
2. `_suppressHide` is false → hide logic runs:
   - Sets `panel.IsOutputHidden = true`, `panel.IsFloating = false`
   - Calls `hideAction()` — this runs `DockManager.OnPanelHiddenWhileFloating(panel)` which sets `_wasFloatingWhenHidden[panel.WindowName] = true`
   - Unchecks Windows menu item
3. Window closes. Docked column remains collapsed (left behind from when the panel was floated — this is intentional; `DockManager.Dock` would have restored it, but the user closed instead of docked).

**Position/size tracking:** Subscribes to `PositionChanged` and `SizeChanged`; on each event, writes `Position` and `ClientSize` to `DockManager`'s in-memory layout state for this panel and triggers a layout save.

### `DockManager`

Central coordinator. Owns all `GameOutputPanel` instances and manages the docked `Grid`.

```csharp
public class DockManager
{
    // Constructed in MainWindow constructor, immediately after InitializeComponent().
    // Creates the Main panel eagerly and calls LoadDefaultLayout() as its last step.
    public DockManager(Grid dockGrid, MenuItem windowsMenu);

    // Exposes Main panel's scroll viewer for CommandInputController
    public ScrollViewer MainScrollViewer { get; }

    // Called by MainWindow text output sites (OnPrintText, OnPrintError, ConnectButton_Click)
    public void Route(Game.WindowTarget target, string targetName,
                      string text, Color fg, Color bg);

    // Called by MainWindow layout menu handlers
    public void SaveLayout(string path);
    public void LoadLayout(string path);
    public void SaveDefaultLayout();
    public void LoadDefaultLayout();

    // Called by the dockAction closure passed to FloatingWindow
    public void Dock(GameOutputPanel panel);

    // Internal
    private GameOutputPanel GetOrCreate(string panelName);
    private void AddToDock(GameOutputPanel panel);
    private void Float(GameOutputPanel panel);
    private void SetVisible(GameOutputPanel panel, bool visible);  // see Windows menu section
    private void PopulateWindowsMenu();
}
```

**Panel lifecycle:**
1. On first `Route()` call for a window target name, `GetOrCreate` creates a `GameOutputPanel(name)`, adds it to the dock as the new rightmost column, and adds a checked menu item to the Windows menu
2. Subsequent `Route()` calls find the existing panel and call `AppendText` (which internally checks `IsOutputHidden`)
3. The Main panel ("main") is created eagerly in the `DockManager` constructor

**`Other` target edge cases:**
- `targetName` null or empty → route to "main"
- `GetOrCreate` uses a single `Dictionary<string, GameOutputPanel>` keyed by panel name. Deduplication is simply: "does a panel with this name already exist in the dictionary?" This covers both static names (from the routing table) and previously-created dynamic `Other` panels. Example: `targetName = "percwindow"` finds the existing ActiveSpells panel (keyed as "percwindow"); `targetName = "activespells"` creates a new dynamic panel (no existing entry). Repeated calls with the same `targetName` always find the same panel.

**`SetVisible` behavior (re-enabling from Windows menu):**
- If `panel.IsOutputHidden == false` already, no-op
- Sets `panel.IsOutputHidden = false`
- If the panel's docked column exists but is collapsed → restore column and paired splitter (makes it visible in dock)
- If the panel was floating when hidden → recreate `FloatingWindow` at last known position
- Updates Windows menu item to checked

The "was floating when hidden" state is tracked in `DockManager` via `Dictionary<string, bool> _wasFloatingWhenHidden` (keyed by `panel.WindowName`). `DockManager.Float` sets `_wasFloatingWhenHidden[panel.WindowName] = false` (clears the flag for this new float cycle). `DockManager` exposes an internal method `void OnPanelHiddenWhileFloating(GameOutputPanel panel)` which sets `_wasFloatingWhenHidden[panel.WindowName] = true`; this method is passed as the `hideAction` closure to `FloatingWindow`. `SetVisible` reads this dictionary.

**Windows menu:**
- `DockManager` holds a reference to the `_Windows` `MenuItem`
- On panel creation, inserts a `MenuItem` with `Header = panel.WindowName`, `IsCheckable = true`, `IsChecked = true`
- Click handler calls `SetVisible(panel, !panel.IsOutputHidden)`
- On show/hide, updates `IsChecked`
- Main panel's menu item is always checked and disabled (cannot be hidden)

### `WindowLayoutState`

Plain serializable record (no external dependencies — use `System.Text.Json`):

```csharp
public record PanelLayoutEntry(
    string WindowName,      // lowercase panel name, e.g. "thoughts", "room"
    bool Visible,
    bool IsFloating,
    double SizeRatio,       // normalized 0–1 proportion of total dock width (if docked; ignored if floating)
    double FloatX,          // screen position (if floating; ignored if docked)
    double FloatY,
    double FloatWidth,
    double FloatHeight
);

public record WindowLayoutState(List<PanelLayoutEntry> Panels);
```

**`SizeRatio` definition:** A value between 0 and 1 representing this panel's share of the total non-splitter dock width (including Main). On save (for ALL columns including Main): `ratio = column.ActualWidth / totalNonSplitterWidth`. On load (for ALL columns including Main): `columnWidth = new GridLength(ratio * 1000, GridUnitType.Star)`. Main's column is treated identically to sub-panel columns — it has no special `"*"` after layout is loaded. On first launch (default layout, no saved file), Main starts as `1*` and sub-panels are added at `0.2 * mainCurrentStarValue` to keep proportions reasonable.

**Case sensitivity:** All `WindowName` values are stored and matched as lowercase. `LoadLayout` compares using `StringComparison.OrdinalIgnoreCase` to tolerate any case in saved files.

**Default `SizeRatio` for new panels:** When a panel appears for the first time (no saved layout entry), default its column star-size to `0.2 * mainCurrentStars`, where `mainCurrentStars` is the current star value of Main's `ColumnDefinition`. For example, if Main is currently `1000*`, the new panel gets `200*`. This gives it roughly 17% of available width while Main retains ~83%.

---

## Docked Layout Model

The dock area is a single `Grid` inside `MainWindow`. No named zones.

**Column arrangement:**
- `Main` panel always occupies the leftmost `ColumnDefinition` with `Width="*"` (fills remaining space). Created eagerly by `DockManager` constructor.
- Each additional docked panel gets its own `ColumnDefinition` with a star-size derived from its saved `SizeRatio`
- Each non-Main panel **owns** a paired splitter `ColumnDefinition` (fixed `Width=4`) that is inserted **immediately to its left**. The splitter is created with the panel and their column indices are permanently associated.

**Column layout example with Main + two panels:**
```
Col 0: Main (*)  | Col 1: Splitter (4) | Col 2: Thoughts (200*) | Col 3: Splitter (4) | Col 4: Room (200*)
```
No splitter to the right of the rightmost panel. A splitter between Main and the first sub-panel is always present when any sub-panel is docked — this is intentional and consistent with the "each panel owns its left splitter" rule.

**Adding a panel (rightmost):**
- Append the panel's paired splitter column at the end of the grid
- Append the panel column after it

**Removing a docked panel** (hide or float):
- Collapse the panel's column (`Width = new GridLength(0)`, `Visibility = Collapsed`)
- Collapse the panel's **own paired splitter** (the one at the fixed column index recorded when this panel was added)
- Each panel always collapses exactly its own paired splitter, regardless of whether neighboring panels are visible or collapsed

**Re-docking a floating panel:**
- Restore the panel's column (`Width` set back to saved star-size, `Visibility = Visible`)
- Restore the panel's own paired splitter
- Panel is restored to its original column index; column order does not change during float/dock cycles

**Size persistence:** Subscribe to `GridSplitter.PointerReleased` as the drag-end signal (Avalonia 11 has no `DragCompleted` on `GridSplitter`). Guard: compare current `ActualWidth` of all non-splitter columns against the last saved in-memory snapshot; only update and persist if any value has changed.

---

## Floating Windows

- `GameOutputPanel.FloatRequested` (fired by Float button) → `DockManager.Float(panel)`
- `DockManager.Float`: collapses panel's dock column + paired splitter, sets `_wasFloatingWhenHidden[panel.WindowName] = false`, sets `panel.IsFloating = true`, creates `FloatingWindow(panel, dockAction: () => this.Dock(panel), hideAction: () => this.OnPanelHiddenWhileFloating(panel))`, shows at last known screen position (or 50px offset from main window top-left corner if floating for the first time)
- Dock sequence: see `FloatingWindow` dock sequence above
- Close/hide sequence: see `FloatingWindow` close/hide sequence above
- `DockManager.Dock(panel)`: sets `panel.IsFloating = false`, restores panel column + paired splitter. `FloatingWindow` closes itself after calling `dockAction()`.

---

## Text Routing

All text output in `MainWindow` is routed through `_dockManager.Route(...)`. The following call sites in `MainWindow.axaml.cs` are updated:

**`OnPrintText`** (was: `AppendOutput(text, color, bgcolor)`, called via `Dispatcher.UIThread.Post`):
```csharp
Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    _dockManager.Route(targetwindow, targetwindowstring, text, color, bgcolor));
```

**`OnPrintError`** (was: `AppendOutput(text)`, called via `Dispatcher.UIThread.Post`):
```csharp
Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    _dockManager.Route(Game.WindowTarget.Main, string.Empty, text, default, default));
```

**`ConnectButton_Click` status messages** (was: `AppendOutput("...")`). There are three call sites:
1. The validation error before the connect path — direct call, already on UI thread:
   ```csharp
   _dockManager.Route(Game.WindowTarget.Main, string.Empty, "[Connect] Account and password are required.\n", default, default);
   ```
2. The status message before `Task.Run` — direct call, already on UI thread:
   ```csharp
   _dockManager.Route(Game.WindowTarget.Main, string.Empty, statusMessage + "\n", default, default);
   ```
3. The error message inside the `catch` handler's `Dispatcher.UIThread.Post` lambda:
   ```csharp
   Avalonia.Threading.Dispatcher.UIThread.Post(() =>
   {
       _dockManager.Route(Game.WindowTarget.Main, string.Empty, $"[Connect error: {ex.Message}]\n", default, default);
       ConnectButton.IsEnabled = true;
   });
   ```

`AppendOutput` is removed from `MainWindow` entirely.

`DockManager.Route` maps `WindowTarget` to panel name:

| WindowTarget    | Panel name             |
|----------------|------------------------|
| Main / Unknown  | "main"                |
| Combat          | "combat"              |
| Portrait        | "portrait"            |
| Inv             | "inv"                 |
| Familiar        | "familiar"            |
| Thoughts        | "thoughts"            |
| Logons          | "logons"              |
| Death           | "death"               |
| Room            | "room"                |
| Log             | "log"                 |
| Raw             | "raw"                 |
| Debug           | "debug"               |
| ActiveSpells    | "percwindow"          |
| Other           | `targetName.ToLower()` (with edge-case handling above) |

---

## `CommandInputController` Scroll Target

`CommandInputController` currently receives `OutputScroll` (a `ScrollViewer` in `MainWindow`) for Ctrl+PageUp/Down scrolling. After this feature `OutputScroll` no longer exists in `MainWindow`. Instead:

- `DockManager` exposes `public ScrollViewer MainScrollViewer { get; }` returning the Main panel's `OutputScroll`
- `MainWindow` passes it to `CommandInputController`:
  ```csharp
  _controller = new CommandInputController(_game, CommandBox, _dockManager.MainScrollViewer);
  ```
- `MainScrollViewer` is available immediately since Main panel is created eagerly in `DockManager` constructor

---

## Layout Persistence

**File location:** `{GenieDataDir}/layout.json`

**Before implementing**, read the Windows version's layout save/load code to determine:
- Whether the JSON schema can be shared
- Whether the file location matches

**Save triggers:**
- Main window closing (`Window.Closing` event)
- Panel floated, docked, shown, or hidden
- `GridSplitter.PointerReleased` (guarded — only save if in-memory column width snapshot has changed)
- `FloatingWindow` `PositionChanged` / `SizeChanged`
- `MenuLayout_SaveDefault` / `MenuLayout_SaveAs` stub handlers

**Load:** `DockManager` constructor calls `LoadDefaultLayout()` as its last step. If the file is missing or invalid JSON, apply default: Main panel visible and docked, all others hidden.

**`MenuLayout_Load`:** `MainWindow.MenuLayout_Load` shows a file-picker dialog (stub — dialog implementation deferred). Once a path is selected, calls `_dockManager.LoadLayout(path)`.

**Default layout** (no saved file):
```json
{
  "Panels": [
    { "WindowName": "main", "Visible": true, "IsFloating": false, "SizeRatio": 1.0,
      "FloatX": 0, "FloatY": 0, "FloatWidth": 0, "FloatHeight": 0 }
  ]
}
```

---

## MainWindow Changes

**`MainWindow.axaml`:**
- Remove `ScrollViewer` (OutputScroll) and `SelectableTextBlock` (OutputText) from the layout
- Replace with an empty `Grid` named `DockGrid`; all columns are added programmatically by `DockManager`
- Add `x:Name="_WindowsMenu"` to the `Windows` top-level `MenuItem`

**`MainWindow.axaml.cs`:**
- Add `private DockManager _dockManager;`
- Constructor order (after `InitializeComponent()`):
  1. `_dockManager = new DockManager(DockGrid, _WindowsMenu);` — creates Main panel eagerly, loads default layout
  2. `_controller = new CommandInputController(_game, CommandBox, _dockManager.MainScrollViewer);`
  3. `_game.EventPrintText += OnPrintText;` — **must come after step 1** so `_dockManager` is not null when the first text event fires
  4. Remaining event subscriptions (`EventDisconnected`, `EventPrintError`, `EventVariableChanged`) and `UpdateWindowTitle()` (unchanged order relative to each other)
- `OnPrintText`: delegate to `_dockManager.Route(...)` (see Text Routing section)
- `OnPrintError`: delegate to `_dockManager.Route(Main, ...)` (see Text Routing section)
- `ConnectButton_Click`: replace `AppendOutput(...)` calls with `_dockManager.Route(Main, ...)` (see Text Routing section)
- Remove `AppendOutput` method entirely
- Wire layout menu stubs:
  - `MenuLayout_SaveDefault` → `_dockManager.SaveDefaultLayout()`
  - `MenuLayout_SaveAs` → show save-file dialog (stub), then `_dockManager.SaveLayout(path)`
  - `MenuLayout_LoadDefault` → `_dockManager.LoadDefaultLayout()`
  - `MenuLayout_Load` → show open-file dialog (stub), then `_dockManager.LoadLayout(path)`
  - `MenuLayout_SaveSizedDefault`, `MenuLayout_Basic`, `MenuLayout_MagicPanels` → no-op stubs (out of scope)

---

## Files Changed

| File | Change |
|------|--------|
| `Desktop/GameOutputPanel.axaml` | New UserControl — title bar (Float/Dock/close buttons) + scroll + text block |
| `Desktop/GameOutputPanel.axaml.cs` | New code-behind — `AppendText`, `IsFloating`, `IsOutputHidden`, events, `OutputScroll` |
| `Desktop/FloatingWindow.axaml` | New Window — minimal AXAML: `<Window xmlns="..." ...>` with no child elements; `Content = panel` is assigned in code-behind |
| `Desktop/FloatingWindow.axaml.cs` | New code-behind — `dockAction` closure, suppress-hide flag, position/size tracking |
| `Desktop/DockManager.cs` | New class — routing, grid management, menu, layout I/O, `MainScrollViewer` |
| `Desktop/WindowLayoutState.cs` | New records — `PanelLayoutEntry`, `WindowLayoutState` |
| `Desktop/MainWindow.axaml` | Replace output area with empty `DockGrid`; name `Windows` menu item |
| `Desktop/MainWindow.axaml.cs` | Add `DockManager`, update constructor order, update all `AppendOutput` call sites, remove `AppendOutput`, wire layout menu stubs |
