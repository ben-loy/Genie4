# Core Text Processing Integration Design

**Date:** 2026-03-16
**Status:** Approved
**Scope:** Bring Avalonia desktop client to parity with WinForms client for initialization, game state, text processing pipeline, and script/trigger execution. AutoMapper and PluginHost integration are explicitly deferred to separate phases.

---

## Goal

The Avalonia `MainWindow` currently subscribes to 4 of 17 Game events and never loads any config data from disk (aliases, triggers, highlights, etc. are all empty at runtime). This spec covers:

1. A full initialization sequence that loads all config and prints progress to the main panel
2. Creation of the `Command` instance (script/trigger execution engine)
3. Wiring of all 13 remaining Game events

---

## Out of Scope

- **AutoMapper** — deferred (separate phase)
- **LegacyPluginHost / PluginHost** — deferred (separate phase)
- **Status bar UI** (vitals, roundtime bar, spell timers) — events subscribed but no UI rendered yet
- **Image display** (`EventAddImage`) — event subscribed, no-op for now

---

## Approach

Mirror the FormMain (WinForms) pattern directly: `MainWindow` owns `Command` as a field, runs the init sequence from the `Opened` event handler, and wires all events inline. No new abstractions or DI changes required.

---

## Section 1: Initialization Sequence

### Trigger

`MainWindow` subscribes to `Opened` (fires once after first render) and calls `await InitializeAsync()`. Running after first render ensures the main panel is visible and rendering before messages appear — matching the WinForms feel.

### Load Sequence

`InitializeAsync()` prints each step to the main panel via a private `AppendInit(string text)` helper, then performs the load. Each step prints `"Loading X..."`, performs the load, then appends `"OK\r\n"` on the same line (or `"FAILED\r\n"` on exception).

```
Using Encoding: Unicode (UTF-8)
Genie User Data Path: {LocalDirectory.Path}

Loading Settings...         → Config.Load(ConfigDir\settings.cfg)        → OK
Loading Presets...          → PresetList.Load(ConfigDir\presets.cfg)      → OK
Loading Global Variables... → VariableList.Load(ConfigDir\variables.cfg)  → OK
Loading Highlights...       → Globals.LoadHighlights(ConfigDir\highlights.cfg) → OK
Loading Names...            → NameList.Load(ConfigDir\names.cfg)          → OK
Loading Macros...           → MacroList.Load(ConfigDir\macros.cfg)        → OK
Loading Aliases...          → AliasList.Load(ConfigDir\aliases.cfg)       → OK
Loading Substitutes...      → SubstituteList.Load(ConfigDir\substitutes.cfg) → OK
Loading Gags...             → GagList.Load(ConfigDir\gags.cfg)            → OK
Loading Triggers...         → TriggerList.Load(ConfigDir\triggers.cfg)    → OK
Loading Classes...          → ClassList.Load(ConfigDir\classes.cfg)       → OK
```

`ConfigDir` is `_game.Globals.Config.ConfigDir` — the same property FormMain uses, so the config location respects whatever the user has configured.

### Error Handling

Each load step is individually wrapped in `try/catch`. On failure, `"FAILED\r\n"` is printed and loading continues. Missing config means empty lists (no aliases, no triggers, etc.) but the app still starts and connects.

### AppendInit Helper

```csharp
private void AppendInit(string text) =>
    _dockManager.Route(Game.WindowTarget.Main, string.Empty,
                       text, Color.WhiteSmoke, Color.Empty);
```

Uses `Color.Empty` for background (inherits panel background). Uses `Color.WhiteSmoke` for foreground, matching the `scriptecho` preset color FormMain uses for init messages.

---

## Section 2: Command and Event Wiring

### Command Creation

After all loads complete, `Command` is instantiated:

```csharp
_command = new Command(ref _game.Globals);
```

Stored as `private Command? _command` on `MainWindow`. Created during init (not construction) because it may depend on loaded config (triggers, aliases).

### Game Events — Active (real behavior)

These are wired during `InitializeAsync()` after `_command` is created. All handlers dispatch to the UI thread via `Dispatcher.UIThread.Post(...)`.

| Event | Signature | Handler |
|---|---|---|
| `EventClearWindow` | `(Game.WindowTarget target)` | `_dockManager.ClearPanel(target)` |
| `EventStreamWindow` | `(string name, ...)` | `_dockManager.EnsureVisible(name)` |
| `EventTriggerParse` | `(string text, Game.WindowTarget target)` | forward to `_command` |
| `EventTriggerPrompt` | `()` | forward to `_command` |
| `EventTriggerMove` | `(string direction)` | forward to `_command` |

The exact `_command` method signatures for the trigger events are determined during implementation by matching FormMain's `Game_EventTriggerParse`, `Game_EventTriggerPrompt`, and `Game_EventTriggerMove` handlers.

### Game Events — Stub (subscribed, no UI yet)

All seven stubs are empty lambdas with a `// TODO` comment naming the future feature:

| Event | Future feature |
|---|---|
| `EventDataReceiveEnd` | End-of-update flush (scrollback, etc.) |
| `EventRoundTime(int ms)` | Roundtime countdown bar |
| `EventCastTime(int ms)` | Cast timer bar |
| `EventSpellTime(...)` | Active spell timer |
| `EventClearSpellTime()` | Clear spell timer display |
| `EventStatusBarUpdate(...)` | Vitals bars (health, mana, concentration, spirit) |
| `EventAddImage(...)` | Inline image display in output panel |

Stub handlers do **not** dispatch to the UI thread (no-op, no allocation needed).

---

## Section 3: Threading and DockManager Additions

### Threading Model

All Game events fire on a background thread. Every **active** handler wraps its body in `Dispatcher.UIThread.Post(...)`. Stub handlers are empty lambdas — no dispatch needed.

Pattern (from existing `OnPrintText`):
```csharp
_game.EventClearWindow += (target) =>
    Avalonia.Threading.Dispatcher.UIThread.Post(() => _dockManager.ClearPanel(target));
```

### DockManager: ClearPanel

New public method resolves the target name (via `TargetMap` for known targets, or direct name for `Other`) and calls `panel.ClearOutput()`.

```csharp
public void ClearPanel(Game.WindowTarget target, string targetName = "")
{
    string name = target == Game.WindowTarget.Other
        ? (string.IsNullOrWhiteSpace(targetName) ? "main" : targetName.ToLower())
        : TargetMap.TryGetValue(target, out var n) ? n : "main";

    if (_panels.TryGetValue(name, out var panel))
        panel.ClearOutput();
}
```

### DockManager: EnsureVisible

Creates the panel if it doesn't exist and makes it visible. Reuses existing `GetOrCreate` + `SetVisible` logic:

```csharp
public void EnsureVisible(string name)
{
    var panel = GetOrCreate(name);
    if (panel.IsOutputHidden)
        SetVisible(panel, true);
}
```

### GameOutputPanel: ClearOutput

Clears the `SelectableTextBlock` inline collection:

```csharp
public void ClearOutput() => OutputText.Inlines?.Clear();
```

### App.axaml.cs

No changes. `Globals` and `Game` remain in DI as-is. `Command` is a `MainWindow` field, consistent with FormMain's structure.

---

## Files Changed

| File | Change |
|---|---|
| `Desktop/MainWindow.axaml.cs` | Add `_command` field, `Opened` handler, `InitializeAsync()`, `AppendInit()`, wire 13 events |
| `Desktop/DockManager.cs` | Add `ClearPanel()`, `EnsureVisible()` |
| `Desktop/GameOutputPanel.axaml.cs` | Add `ClearOutput()` |
| `Desktop/App.axaml.cs` | No changes |

---

## Verification

1. App starts and prints full init sequence to main panel
2. Config files load — aliases, triggers, macros, highlights are active after connecting
3. Aliases expand when typed (e.g. a short alias fires its expansion)
4. Triggers fire on matching server text (trigger command executes)
5. Scripts launch via the script character prefix
6. `EventClearWindow` clears the correct panel
7. `EventStreamWindow` creates/shows a named panel
8. All stub events are wired (no unsubscribed event warnings in logs)
