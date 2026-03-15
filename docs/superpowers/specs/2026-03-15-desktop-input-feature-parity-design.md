# Desktop Input Box Feature Parity — Design Spec

**Date:** 2026-03-15
**Feature:** Full feature parity between the Desktop (`CommandBox`) and WinForms (`ComponentTextBox`) input boxes
**Scope:** Command history, tab completion (aliases + scripts), smart paste, PageUp/Down output scrolling, KeepInput mode

---

## Goal

Replace the minimal `CommandBox_KeyDown` stub in `MainWindow` with a fully-featured input controller that matches the WinForms `ComponentTextBox` behaviour: command history navigation, tab completion cycling, smart paste, output scrolling shortcuts, and KeepInput mode.

---

## Background

### WinForms (`ComponentTextBox.cs`)

A custom `RichTextBox` subclass (~284 lines) that owns all input logic: a history `ArrayList`, Up/Down/Ctrl+Enter navigation, a tab-completion state machine, Ctrl+V paste interception, and PageUp/Down event forwarding.

### Avalonia Desktop (current)

`MainWindow.CommandBox_KeyDown` handles only `Key.Enter` — sends the command and clears the box. All other keys fall through to default `TextBox` behaviour.

---

## Architecture

**New file:** `Desktop/CommandInputController.cs`

**Modified files:**
- `Desktop/MainWindow.axaml.cs` — constructor instantiates the controller; `CommandBox_KeyDown` becomes a one-liner delegate.
- `Core/Game.cs` — add one public accessor: `public Globals Globals => m_oGlobals;`

`CommandInputController` receives its three dependencies at construction time and acts directly on them. It raises no events and has no rendering concerns.

```
CommandInputController(Game game, TextBox commandBox, ScrollViewer outputScroll)
├── HandleKeyDown(KeyEventArgs)   ← replaces CommandBox_KeyDown body
├── History state                 ← List<string>, position, pending input
└── Tab completion state          ← pattern, matches, index, last-was-tab flag
```

`MainWindow` changes:

```csharp
// In constructor, after existing wiring:
_controller = new CommandInputController(_game, CommandBox, OutputScroll);

// Replace CommandBox_KeyDown body:
private void CommandBox_KeyDown(object? sender, KeyEventArgs e)
    => _controller.HandleKeyDown(e);
```

`Game.cs` change (minimal — one line added to the class body):

```csharp
public Globals Globals => m_oGlobals;
```

---

## Command History

### State

```csharp
private readonly List<string> _history = new();  // index 0 = most recent
private int _historyPos = -1;                     // -1 = "at new input"
private string _pendingInput = string.Empty;      // saved when nav begins
```

### Rules

- **Max size:** 20 entries. When the list exceeds 20, remove the oldest (`_history.RemoveAt(_history.Count - 1)`).
- **Min length:** 3 characters. Commands shorter than 3 chars are not added to history.
- **Deduplication:** If the command equals `_history[0]` (most recent), do not add it again.

### Sending a command (`Enter` key)

1. Read `CommandBox.Text`; if blank, do nothing.
2. Call `_game.SendText(text, bUserInput: true)`.
3. Add to history (applying min-length and deduplication rules).
4. Reset `_historyPos = -1` and `_pendingInput = string.Empty`.
5. Reset tab completion state.
6. If `_game.Globals.Config.bKeepInput` is `true`: `CommandBox.SelectAll()`. Otherwise: `CommandBox.Clear()`.

> **Access note:** Throughout this spec, `_game.Globals` refers to the public accessor `public Globals Globals => m_oGlobals` added to `Game.cs` in the Architecture section above.

### Up arrow

1. If `_historyPos == -1`, capture `CommandBox.Text` into `_pendingInput`.
2. Increment `_historyPos`; clamp to `_history.Count - 1`.
3. Write `_history[_historyPos]` into `CommandBox.Text`; move caret to end.
4. Set `e.Handled = true`.

### Down arrow

1. Decrement `_historyPos`; clamp to `-1`.
2. If `_historyPos == -1`, restore `_pendingInput` into `CommandBox.Text`. Otherwise write `_history[_historyPos]`.
3. Move caret to end.
4. Set `e.Handled = true`.

### Ctrl+Enter

1. If `_history.Count == 0`, do nothing.
2. Write `_history[0]` into `CommandBox.Text`.
3. Execute the send logic (same as Enter key above).
4. Set `e.Handled = true`.

---

## Tab Completion

### State

```csharp
private bool _lastKeyWasTab;
private string _tabPattern = string.Empty;
private List<string> _tabMatches = new();
private int _tabIndex;
```

### Session reset

Any key that is not `Tab` resets: `_lastKeyWasTab = false`, `_tabPattern = string.Empty`, `_tabMatches.Clear()`, `_tabIndex = 0`.

### Completion context selection

Evaluate `CommandBox.Text` at the moment Tab is pressed using these three sequential checks:

1. **Script context:** text starts with `_game.Globals.Config.ScriptChar` **and** does not end with a space.
   - Extract prefix = text after `ScriptChar`.
   - Search `_game.Globals.Config.ScriptDir` for files whose name (without extension) starts with the prefix (case-insensitive).
   - `_tabMatches` = sorted list of matching filenames (without extension).

2. **Trailing-space guard:** if text ends with a space, Tab does nothing (return immediately). This applies whether or not the text starts with `ScriptChar`.

3. **Alias context:** all remaining cases (text does not start with `ScriptChar` and does not end with a space).
   - Iterate `_game.Globals.AliasList.Keys`, casting each entry to `string`. Collect those that start with the current text (case-insensitive, `StringComparison.OrdinalIgnoreCase`).
   - `_tabMatches` = sorted list of matching alias keys.

### Tab key logic

On `Tab` press (`e.Handled = true` always):

1. If `!_lastKeyWasTab`: capture `CommandBox.Text` as `_tabPattern`; populate `_tabMatches` per context above; set `_tabIndex = 0`.
2. If `_tabMatches.Count == 0`: do nothing (no matches).
3. If `_tabMatches.Count == 1`: write `_tabMatches[0] + " "` into `CommandBox.Text`; move caret to end; set `_lastKeyWasTab = false` (session complete).
4. If `_tabMatches.Count > 1`: write `_tabMatches[_tabIndex]` into `CommandBox.Text`; move caret to end; increment `_tabIndex` (wrapping: `_tabIndex = (_tabIndex + 1) % _tabMatches.Count`); set `_lastKeyWasTab = true`.

---

## Smart Paste (Ctrl+V)

1. Set `e.Handled = true`.
2. Get clipboard text via `await TopLevel.GetTopLevel(CommandBox)!.Clipboard!.GetTextAsync()`. If null or empty, return.
3. If text length > 100: show `await MessageBoxManager` (or a simple Avalonia dialog) asking "Paste [N] characters?" — cancel if user declines.
4. Strip all `'\r'` and `'\n'` from text; trim trailing whitespace.
5. Insert at caret: remove `CommandBox.SelectedText` if any, then insert cleaned text at `CommandBox.CaretIndex`.

**Note:** Avalonia's clipboard API is async. `HandleKeyDown` will be declared `async void` to support this.

**Concurrency policy:** Key events that arrive while a Ctrl+V clipboard `await` is in flight are processed normally by Avalonia (they are not suppressed). Any such interleaved events may update `_historyPos`, `_lastKeyWasTab`, or `CommandBox.Text` before the paste resumes. This is acceptable — the MUD input box is single-user and real-time; the window of interleaving is tiny and last-writer-wins is a fine outcome.

---

## PageUp / PageDown Output Scrolling

All four shortcuts set `e.Handled = true`.

| Key | Action |
|-----|--------|
| `PageUp` | `OutputScroll.Offset = new Vector(OutputScroll.Offset.X, Math.Max(0, OutputScroll.Offset.Y - OutputScroll.Viewport.Height))` |
| `PageDown` | `OutputScroll.Offset = new Vector(OutputScroll.Offset.X, OutputScroll.Offset.Y + OutputScroll.Viewport.Height)` |
| `Ctrl+PageUp` | `OutputScroll.Offset = new Vector(OutputScroll.Offset.X, 0)` |
| `Ctrl+PageDown` | `OutputScroll.Offset = new Vector(OutputScroll.Offset.X, double.MaxValue)` |

---

## KeepInput Mode

Controlled by `_game.Globals.Config.bKeepInput` (existing `Config` property, shared with WinForms).

- `true` → after sending: `CommandBox.SelectAll()` (text remains, all selected, ready to be overwritten)
- `false` → after sending: `CommandBox.Clear()`

---

## Key Event Summary

| Key | Action | Resets history nav? | Resets tab? |
|-----|--------|---------------------|-------------|
| `Enter` | Send command | Yes | Yes |
| `Ctrl+Enter` | Recall + send last | Yes | Yes |
| `↑` | History back | No (navigates) | Yes |
| `↓` | History forward | No (navigates) | Yes |
| `Tab` | Cycle completion | Yes | No (cycles) |
| `PageUp/Down` | Scroll output | Yes | Yes |
| `Ctrl+V` | Smart paste | Yes | Yes |
| Any other key | Default TextBox | Yes | Yes |

---

## Files Changed

| File | Change |
|------|--------|
| `Desktop/CommandInputController.cs` | New file — all input logic |
| `Desktop/MainWindow.axaml.cs` | Add `_controller` field; wire in constructor; delegate `CommandBox_KeyDown` |
| `Desktop/MainWindow.axaml` | No change |
| `Core/Game.cs` | Add `public Globals Globals => m_oGlobals;` accessor |

---

## Out of Scope

- History size / min-length configuration UI (follows font/color settings sub-project)
- Macro / key-binding support (separate sub-project)
- `#edit` command tab completion (uses same logic as script completion; deferring to keep scope tight)
- Configurable input font / colors (follow-on sub-project)
- Multiline input
