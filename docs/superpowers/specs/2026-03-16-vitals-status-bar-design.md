# Vitals & Status Bar — Avalonia Design Spec

**Date:** 2026-03-16
**Feature:** Vitals bar, status labels row, and timer bars — WinForms parity for Avalonia desktop client
**Reference:** `Forms/FormMain.cs`, `Forms/Components/ComponentBars.cs`, `Forms/Components/ComponentRoundtime.cs`

---

## Overview

Add three rows below the game output area (above the existing `DockGrid`) to match the WinForms layout:

1. **Status row** — RT timer bar | Left Hand | Right Hand | Spell (with elapsed) | SpellTimer countdown bar
2. **Command input** — existing `CommandBox` (repositioned in DockPanel declaration order)
3. **Vitals row** — Health · Mana · Concentration · Fatigue · Spirit bars

---

## Layout Structure

`MainWindow.axaml` uses a `DockPanel`. Bottom-docked children stack upward in declaration order. The existing `CommandBox` declaration (`DockPanel.Dock="Bottom"`) must be **removed from its current position** in the file and re-declared between the two new rows as shown below. The new declaration order for bottom-docked items:

```xml
<!-- 1. Vitals row — very bottom (declared first) -->
<Grid DockPanel.Dock="Bottom" Height="22" x:Name="VitalsRow">...</Grid>

<!-- 2. Command input — above vitals (declared second) -->
<TextBox DockPanel.Dock="Bottom" x:Name="CommandBox" ... />

<!-- 3. Status row — above command input (declared third) -->
<Grid DockPanel.Dock="Bottom" Height="22" x:Name="StatusRow">...</Grid>

<!-- DockGrid — fills remaining space (last child, unchanged) -->
<Grid x:Name="DockGrid" ... />
```

Visual result (top → bottom):
```
┌────────────────────────────────────┐
│ Menu bar                           │
│ Connect bar                        │
│                                    │
│ DockGrid (game panels)             │
│                                    │
│ StatusRow   (22px)                 │
│ CommandBox                         │
│ VitalsRow   (22px)                 │
└────────────────────────────────────┘
```

---

## New Controls

### `VitalBarControl` — `Desktop/Controls/VitalBarControl.axaml`

A reusable UserControl for a single vital stat bar.

**AXAML structure:** A `Grid` with two star-width columns:
- Column 0: `Value*` — `Border` with `FillColor` background (the filled portion)
- Column 1: `(100 - Value)*` — `Border` with `EmptyColor` background (the unfilled portion)
- `TextBlock` spanning both columns, centered, white text, 10px monospace font

**Properties (code-behind):**

| Property | Type | Description |
|---|---|---|
| `Value` | `int` (0–100) | Current value; changing it updates column widths immediately |
| `BarText` | `string` | Text displayed centered over the bar |
| `FillColor` | `IBrush` | Color of the filled portion |
| `EmptyColor` | `IBrush` | Color of the unfilled portion |
| `IsConnected` | `bool` | When false, both colors convert to grayscale equivalents |

**Default colors per vital (matching WinForms exactly):**

| Vital | FillColor | EmptyColor |
|---|---|---|
| Health | `Maroon` (#800000) | `#400000` |
| Mana | `Navy` (#000080) | `#000040` |
| Concentration | `Teal` (#008080) | `#004040` |
| Fatigue/Stamina | `Green` (#008000) | `#004000` |
| Spirit | `Purple` (#800080) | `#400040` |

**Grayscale:** When `IsConnected = false`, convert both fill and empty colors to grayscale. `Genie.ColorCode.ColorToGrayscale` is accessible from the Desktop project (included in `.csproj`), but it operates on `System.Drawing.Color`, not Avalonia types. The controls store colors as `IBrush` (`SolidColorBrush`). The round-trip is:
1. Extract `Avalonia.Media.Color` from the `SolidColorBrush`
2. Construct a `System.Drawing.Color` from its R/G/B bytes
3. Call `ColorCode.ColorToGrayscale(drawingColor)` → returns `System.Drawing.Color`
4. Construct a new `SolidColorBrush(Avalonia.Media.Color.FromRgb(g.R, g.G, g.B))`

Alternatively, compute inline: `byte lum = (byte)(r * 0.299 + g * 0.587 + b * 0.114)` and use `Color.FromRgb(lum, lum, lum)` to avoid the type round-trip.

---

### `TimerBarControl` — `Desktop/Controls/TimerBarControl.axaml`

A reusable UserControl for RT and SpellTimer countdown bars. Same rendering pattern as `VitalBarControl` but driven by `Remaining`/`Total` instead of `Value`.

**AXAML structure:** Three overlapping layers in a `Grid`:
- `Border` (background): black when `Total == 0`, otherwise `#000000`
- `Border` (fill, `HorizontalAlignment="Left"`): width = `(Remaining / Total) * ActualWidth`; color = `FillColor`
- `TextBlock` centered: `Remaining.ToString()` when `Remaining > 0`, empty string when `Remaining <= 0`

**Properties:**

| Property | Type | Description |
|---|---|---|
| `Remaining` | `int` | Seconds remaining; ≤ 0 → blank, no fill |
| `Total` | `int` | Starting total for fill proportion |
| `FillColor` | `IBrush` | Fill bar color |
| `IsConnected` | `bool` | Grayscale when false |

**Instances and colors:**

| Control | FillColor | Source |
|---|---|---|
| RT bar | `MediumBlue` | `PresetList["roundtime"].FgColor` |
| SpellTimer bar | `Magenta` | `PresetList["castbar"].FgColor` |

`PresetList[key].FgColor` returns `System.Drawing.Color`. Convert to an Avalonia `SolidColorBrush` before assigning to `FillColor`:
```csharp
var c = _game.Globals.PresetList["roundtime"].FgColor;
_rtBar.FillColor = new SolidColorBrush(Avalonia.Media.Color.FromRgb(c.R, c.G, c.B));
```
This conversion is done once during `InitializeAsync()` after presets are loaded.

Both use black backgrounds. Fill shrinks left-to-right as `Remaining` decreases (fill width = `Remaining / Total * totalWidth`).

---

## Status Row Layout

The `StatusRow` `Grid` has 5 columns:

```
┌────────────────┬──────────┬──────────┬──────────────────┬────────────────┐
│ ████░░  RT:3   │ L  Empty │ R  sword │ (14) Fire Ball   │ ████░░  8      │
│  MediumBlue    │          │          │                  │    Magenta     │
└────────────────┴──────────┴──────────┴──────────────────┴────────────────┘
    72px fixed        *          *               *                *
```

| Column | Width | Content | Control |
|---|---|---|---|
| 0 — RT | 72px | Roundtime countdown bar | `TimerBarControl` (`_rtBar`) |
| 1 — LH | `*` | Left hand item | `TextBlock` (`_labelLH`) |
| 2 — RH | `*` | Right hand item | `TextBlock` (`_labelRH`) |
| 3 — Spell | `*` | `(elapsed) SpellName` counting up | `TextBlock` (`_labelSpell`) |
| 4 — SpellTimer | `*` | Cast countdown bar | `TimerBarControl` (`_spellTimerBar`) |

All cells have a 1px right border separator (`#2A2A2A`) except the last. Background: `#111111`. Monospace font, 10px.

---

## Vitals Row Layout

The `VitalsRow` `Grid` has 5 equal star-width columns. Background `#000000`, height 22px.

Order (left to right, matching WinForms `_PanelBars` / screenshot):

| Column | Vital | WinForms control |
|---|---|---|
| 0 | Health | `_ComponentBarsHealth` |
| 1 | Mana | `_ComponentBarsMana` |
| 2 | Concentration | `_ComponentBarsConc` |
| 3 | Fatigue/Stamina | `_ComponentBarsFatigue` |
| 4 | Spirit | `_ComponentBarsSpirit` |

---

## Event Wiring — `MainWindow.axaml.cs`

### New Fields

```csharp
// Status row controls
private TimerBarControl _rtBar;
private TextBlock _labelLH, _labelRH, _labelSpell;
private TimerBarControl _spellTimerBar;

// Vital bar controls
private VitalBarControl _vitalsHealth, _vitalsMana, _vitalsConc, _vitalsFatigue, _vitalsSpirit;

// RT tracking
private int _rtStart;

// SpellTimer tracking
private int _castTotal; // casttime - spellstarttime
```

### Existing Stub Expansions

**`EventVariableChanged(string sVariable)`** — add cases. All values are read from `_game.Globals.VariableList[key]?.ToString() ?? ""`:

| Variable | Action |
|---|---|
| `$health` | `_vitalsHealth.Value = int.Parse(VariableList["health"])`; `_vitalsHealth.BarText = VariableList["healthBarText"]` |
| `$mana` | `_vitalsMana.Value = int.Parse(VariableList["mana"])`; `BarText = VariableList["manaBarText"]` |
| `$spirit` | `_vitalsSpirit.Value = int.Parse(VariableList["spirit"])`; `BarText = VariableList["spiritBarText"]` |
| `$stamina` | `_vitalsFatigue.Value = int.Parse(VariableList["stamina"])`; `BarText = VariableList["staminaBarText"]` |
| `$concentration` | `_vitalsConc.Value = int.Parse(VariableList["concentration"])`; `BarText = VariableList["concentrationBarText"]` |
| `$lefthand` | `_labelLH.Text = "L  " + VariableList["lefthand"]` |
| `$righthand` | `_labelRH.Text = "R  " + VariableList["righthand"]` |
| `$preparedspell` | stored for spell label; refreshed via `UpdateSpellLabel()` |
| `$connected` | `bool isConn = VariableList["connected"]?.ToString() == "1";` — set `IsConnected = isConn` on all 5 vitals + both timer bars |

**`EventStatusBarUpdate()`** — calls `UpdateStatusLabels()` which refreshes LH, RH, Spell.

**`EventRoundTime(int iTime)`** — two actions required:
1. Set `_rtStart = (int)(iTime + _game.Globals.Config.dRTOffset)` for the fill proportion denominator.
2. Set `_game.Globals.RoundTimeEnd = DateTime.Now.AddMilliseconds(iTime * 1000 + _game.Globals.Config.dRTOffset * 1000)` — this is **not** set by `Game.cs`; `FormMain.cs` sets it in its own `SetRoundTime` handler (line 6544). The Avalonia handler must do the same.

RT bar is then driven by `RoundTimeEnd` each tick.

**`EventCastTime()`** — compute `_castTotal = casttime - spellstarttime` from `Globals.VariableList`. Reset `_spellTimerBar.Total = _castTotal`, `_spellTimerBar.Remaining = _castTotal`.

### New Events to Wire

```csharp
_game.EventSpellTime      += OnEventSpellTime;      // SpellTimeStart = DateTime.Now
_game.EventClearSpellTime += OnEventClearSpellTime; // SpellTimeStart = DateTime.MinValue
```

Both events already declared in `Game.cs` (lines 58–64) and wired in `FormMain.cs` (lines 394–420). Currently unwired in `MainWindow.axaml.cs`.

### Game Loop Tick Additions

Inside existing `OnGameLoopTick`, add after existing script/command processing:

```csharp
// 1. RT bar
int rtRemaining = (int)Math.Max(0, Math.Ceiling(
    (_game.Globals.RoundTimeEnd - DateTime.Now).TotalSeconds));
_rtBar.Remaining = rtRemaining;
_rtBar.Total = _rtStart;

// 2. SpellTimer bar
if (_castTotal > 0 && _game.Globals.SpellTimeStart != DateTime.MinValue)
{
    double castElapsed = (DateTime.Now - _game.Globals.SpellTimeStart).TotalSeconds;
    int castRemaining = (int)Math.Max(0, Math.Ceiling(_castTotal - castElapsed));
    _spellTimerBar.Remaining = castRemaining;
    _spellTimerBar.Total = _castTotal;
}

// 3. Spell elapsed label (S column)
if (_game.Globals.SpellTimeStart != DateTime.MinValue)
{
    int spellElapsed = (int)(DateTime.Now - _game.Globals.SpellTimeStart).TotalSeconds;
    UpdateSpellLabel(spellElapsed);
}
```

**`UpdateSpellLabel(int elapsed)`:**
```csharp
string spell = _game.Globals.VariableList["preparedspell"]?.ToString() ?? "";
_labelSpell.Text = (Config.bShowSpellTimer && elapsed > 0 && spell != "None")
    ? $"({elapsed}) {spell}"
    : spell;
```

---

## Disconnected State

When `$connected` changes, read `_game.Globals.VariableList["connected"]?.ToString() == "1"` to get a bool. When false, all vitals and timer bars have `IsConnected = false` set, which renders them in grayscale. This matches WinForms where `ComponentBars.IsConnected` and `ComponentRoundtime.IsConnected` gray out the controls. Note: `"connected"` is stored as `"0"` or `"1"` in `VariableList`, not as a boolean.

---

## Files Changed / Created

| File | Change |
|---|---|
| `Desktop/Controls/VitalBarControl.axaml` | New UserControl |
| `Desktop/Controls/VitalBarControl.axaml.cs` | New code-behind |
| `Desktop/Controls/TimerBarControl.axaml` | New UserControl |
| `Desktop/Controls/TimerBarControl.axaml.cs` | New code-behind |
| `Desktop/MainWindow.axaml` | Add StatusRow, reorder CommandBox, add VitalsRow |
| `Desktop/MainWindow.axaml.cs` | New fields, expand stubs, wire new events, game loop additions |

No changes to `Game.cs`, `Globals.cs`, or any WinForms files.
