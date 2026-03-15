# Desktop Input Box Feature Parity — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the minimal `CommandBox_KeyDown` stub in `MainWindow` with a `CommandInputController` that provides command history, tab completion, smart paste, PageUp/Down output scrolling, and KeepInput mode.

**Architecture:** A new `Desktop/CommandInputController.cs` owns all input state and logic. `MainWindow` holds a `_controller` reference and delegates `CommandBox_KeyDown` to it. `Game.cs` gets one public `Globals` accessor so the controller can read `Config` and `AliasList`.

**Tech Stack:** Avalonia 11.3.12, .NET 10, C#. No new NuGet packages required.

---

## File Map

| File | Change |
|------|--------|
| `Core/Game.cs` | Add `public Globals Globals => m_oGlobals;` after the private property (~line 161) |
| `Desktop/CommandInputController.cs` | **New file** — all input logic: history, tab completion, smart paste, scrolling |
| `Desktop/MainWindow.axaml.cs` | Add `_controller` field; instantiate in constructor; delegate `CommandBox_KeyDown` |

No other files change.

---

## Chunk 1: Foundation

### Task 1: Expose `Globals` via `Game.cs`

**Files:**
- Modify: `Core/Game.cs`

- [ ] **Step 1: Locate the insertion point in `Core/Game.cs`**

  Find the private `m_oGlobals` property (around line 138). Its closing `}` is followed by:

  ```csharp
          }
      }

      private bool m_bShowRawOutput = false;
  ```

- [ ] **Step 2: Add the public accessor**

  Insert one line between the `}` that closes `m_oGlobals` and `private bool m_bShowRawOutput`:

  ```csharp
  public Globals Globals => m_oGlobals;
  ```

  Result in context:
  ```csharp
          }
      }

      public Globals Globals => m_oGlobals;

      private bool m_bShowRawOutput = false;
  ```

- [ ] **Step 3: Build**

  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj --nologo -v quiet 2>&1 | tail -3
  ```

  Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

  ```bash
  git add Core/Game.cs
  git commit -m "feat: expose Globals via public accessor on Game"
  ```

---

### Task 2: Create `CommandInputController.cs` and wire `MainWindow`

**Files:**
- Create: `Desktop/CommandInputController.cs`
- Modify: `Desktop/MainWindow.axaml.cs`

- [ ] **Step 1: Create `Desktop/CommandInputController.cs`** with the full implementation below

  ```csharp
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using Avalonia;
  using Avalonia.Controls;
  using Avalonia.Input;
  using Avalonia.Layout;
  using Avalonia.Media;
  using GenieClient.Genie;

  namespace GenieClient.Desktop;

  internal sealed class CommandInputController
  {
      private readonly Game         _game;
      private readonly TextBox      _commandBox;
      private readonly ScrollViewer _outputScroll;

      // History (index 0 = most recent)
      private readonly List<string> _history = new();
      private int    _historyPos   = -1;           // -1 = "at new input"
      private string _pendingInput = string.Empty; // saved when nav begins

      // Tab completion
      private bool         _lastKeyWasTab;
      private string       _tabPattern = string.Empty;
      private List<string> _tabMatches = new();
      private int          _tabIndex;

      public CommandInputController(Game game, TextBox commandBox, ScrollViewer outputScroll)
      {
          _game         = game;
          _commandBox   = commandBox;
          _outputScroll = outputScroll;
      }

      public async void HandleKeyDown(KeyEventArgs e)
      {
          bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

          switch (e.Key)
          {
              case Key.Enter when ctrl:
                  HandleCtrlEnter(e);
                  break;

              case Key.Enter:
                  HandleEnter(e);
                  break;

              case Key.Up:
                  ResetTabState();
                  HandleUp(e);
                  break;

              case Key.Down:
                  ResetTabState();
                  HandleDown(e);
                  break;

              case Key.Tab:
                  ResetHistoryNav();
                  HandleTab(e);
                  break;

              case Key.PageUp when ctrl:
                  ResetTabState(); ResetHistoryNav();
                  _outputScroll.Offset = new Vector(_outputScroll.Offset.X, 0);
                  e.Handled = true;
                  break;

              case Key.PageDown when ctrl:
                  ResetTabState(); ResetHistoryNav();
                  _outputScroll.Offset = new Vector(_outputScroll.Offset.X, double.MaxValue);
                  e.Handled = true;
                  break;

              case Key.PageUp:
                  ResetTabState(); ResetHistoryNav();
                  _outputScroll.Offset = new Vector(
                      _outputScroll.Offset.X,
                      Math.Max(0, _outputScroll.Offset.Y - _outputScroll.Viewport.Height));
                  e.Handled = true;
                  break;

              case Key.PageDown:
                  ResetTabState(); ResetHistoryNav();
                  _outputScroll.Offset = new Vector(
                      _outputScroll.Offset.X,
                      _outputScroll.Offset.Y + _outputScroll.Viewport.Height);
                  e.Handled = true;
                  break;

              case Key.V when ctrl:
                  ResetTabState(); ResetHistoryNav();
                  await HandlePasteAsync(e);
                  break;

              default:
                  ResetTabState();
                  ResetHistoryNav();
                  break;
          }
      }

      // ── Send ─────────────────────────────────────────────────────────────────

      private void HandleEnter(KeyEventArgs e)
      {
          string text = _commandBox.Text ?? string.Empty;
          if (string.IsNullOrEmpty(text)) { e.Handled = true; return; }
          ExecuteSend(text);
          e.Handled = true;
      }

      private void HandleCtrlEnter(KeyEventArgs e)
      {
          if (_history.Count == 0) { e.Handled = true; return; }
          string text = _history[0];
          _commandBox.Text = text;
          ExecuteSend(text);
          e.Handled = true;
      }

      private void ExecuteSend(string text)
      {
          _game.SendText(text, bUserInput: true);
          AddToHistory(text);
          ResetHistoryNav();
          ResetTabState();
          if (_game.Globals.Config.bKeepInput)
              _commandBox.SelectAll();
          else
              _commandBox.Clear();
      }

      // ── History ───────────────────────────────────────────────────────────────

      private void AddToHistory(string text)
      {
          if (text.Length < 3) return;
          if (_history.Count > 0 && _history[0] == text) return;
          _history.Insert(0, text);
          if (_history.Count > 20)
              _history.RemoveAt(_history.Count - 1);
      }

      private void HandleUp(KeyEventArgs e)
      {
          if (_history.Count == 0) { e.Handled = true; return; }
          if (_historyPos == -1)
              _pendingInput = _commandBox.Text ?? string.Empty;
          _historyPos = Math.Min(_historyPos + 1, _history.Count - 1);
          _commandBox.Text = _history[_historyPos];
          _commandBox.CaretIndex = _commandBox.Text.Length;
          e.Handled = true;
      }

      private void HandleDown(KeyEventArgs e)
      {
          if (_historyPos == -1) { e.Handled = true; return; }
          _historyPos--;
          _commandBox.Text = _historyPos == -1 ? _pendingInput : _history[_historyPos];
          _commandBox.CaretIndex = (_commandBox.Text ?? string.Empty).Length;
          e.Handled = true;
      }

      private void ResetHistoryNav()
      {
          _historyPos   = -1;
          _pendingInput = string.Empty;
      }

      // ── Tab Completion ────────────────────────────────────────────────────────

      private void HandleTab(KeyEventArgs e)
      {
          string text = _commandBox.Text ?? string.Empty;

          if (!_lastKeyWasTab)
          {
              _tabPattern = text;
              _tabMatches = BuildTabMatches(text);
              _tabIndex   = 0;
          }

          e.Handled = true;

          if (_tabMatches.Count == 0) return;

          if (_tabMatches.Count == 1)
          {
              _commandBox.Text = _tabMatches[0] + " ";
              _commandBox.CaretIndex = _commandBox.Text.Length;
              _lastKeyWasTab = false;
              return;
          }

          _commandBox.Text = _tabMatches[_tabIndex];
          _commandBox.CaretIndex = _commandBox.Text.Length;
          _tabIndex      = (_tabIndex + 1) % _tabMatches.Count;
          _lastKeyWasTab = true;
      }

      private List<string> BuildTabMatches(string text)
      {
          // Guard 1: script context (starts with ScriptChar, no trailing space)
          if (text.Length > 0
              && text[0] == _game.Globals.Config.ScriptChar
              && !text.EndsWith(' '))
          {
              string prefix    = text[1..];
              string scriptDir = _game.Globals.Config.ScriptDir;
              if (!Directory.Exists(scriptDir)) return new List<string>();
              return Directory.GetFiles(scriptDir)
                  .Select(Path.GetFileNameWithoutExtension)
                  .Where(n => n != null && n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                  .Cast<string>()
                  .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                  .ToList();
          }

          // Guard 2: trailing space — nothing to complete
          if (text.EndsWith(' ')) return new List<string>();

          // Guard 3: alias context
          var matches = new List<string>();
          foreach (object key in _game.Globals.AliasList.Keys)
          {
              string k = (string)key;
              if (k.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                  matches.Add(k);
          }
          matches.Sort(StringComparer.OrdinalIgnoreCase);
          return matches;
      }

      private void ResetTabState()
      {
          _lastKeyWasTab = false;
          _tabPattern    = string.Empty;
          _tabMatches.Clear();
          _tabIndex = 0;
      }

      // ── Smart Paste ───────────────────────────────────────────────────────────

      private async Task HandlePasteAsync(KeyEventArgs e)
      {
          e.Handled = true;

          var topLevel = TopLevel.GetTopLevel(_commandBox);
          if (topLevel?.Clipboard == null) return;

          string? raw = await topLevel.Clipboard.GetTextAsync();
          if (string.IsNullOrEmpty(raw)) return;

          if (raw.Length > 100)
          {
              bool confirmed = await ConfirmPasteAsync(raw.Length, topLevel as Window);
              if (!confirmed) return;
          }

          // Strip CR/LF; trim trailing whitespace
          string cleaned = raw.Replace("\r", "").Replace("\n", "").TrimEnd();

          int    start   = _commandBox.SelectionStart;
          int    end     = _commandBox.SelectionEnd;
          string current = _commandBox.Text ?? string.Empty;

          // SelectedText is read-only in Avalonia 11; splice via Text assignment
          _commandBox.Text       = current[..start] + cleaned + current[end..];
          _commandBox.CaretIndex = start + cleaned.Length;
      }

      private static async Task<bool> ConfirmPasteAsync(int charCount, Window? owner)
      {
          // If no owner window is available, allow paste silently
          if (owner == null) return true;

          var yesBtn = new Button { Content = "Yes" };
          var noBtn  = new Button { Content = "No"  };

          var dialog = new Window
          {
              Title                 = "Confirm Paste",
              Width                 = 340,
              Height                = 110,
              CanResize             = false,
              WindowStartupLocation = WindowStartupLocation.CenterOwner,
              Content = new StackPanel
              {
                  Margin   = new Thickness(16),
                  Spacing  = 12,
                  Children =
                  {
                      new TextBlock
                      {
                          Text         = $"Paste {charCount} characters?",
                          TextWrapping = TextWrapping.Wrap
                      },
                      new StackPanel
                      {
                          Orientation         = Orientation.Horizontal,
                          HorizontalAlignment = HorizontalAlignment.Right,
                          Spacing             = 8,
                          Children            = { yesBtn, noBtn }
                      }
                  }
              }
          };

          // Close(result) sets the ShowDialog<T> return value
          yesBtn.Click += (_, _) => dialog.Close(true);
          noBtn.Click  += (_, _) => dialog.Close(false);

          return await dialog.ShowDialog<bool>(owner);
      }
  }
  ```

- [ ] **Step 2: Build**

  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj --nologo -v quiet 2>&1 | tail -3
  ```

  Expected: `0 Error(s)`

- [ ] **Step 3: Modify `Desktop/MainWindow.axaml.cs`**

  **3a.** After line 16 (`private readonly Game _game;`), add:

  ```csharp
  private readonly CommandInputController _controller;
  ```

  **3b.** At the end of the constructor body, after `_game.EventPrintError += OnPrintError;` and before the closing `}`, add:

  ```csharp
  _controller = new CommandInputController(_game, CommandBox, OutputScroll);
  ```

  **3c.** Replace the entire `CommandBox_KeyDown` method body (currently lines 62–74):

  ```csharp
  private void CommandBox_KeyDown(object? sender, KeyEventArgs e)
      => _controller.HandleKeyDown(e);
  ```

  The old body with `if (e.Key == Key.Enter) { … }` is completely removed.

- [ ] **Step 4: Build**

  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj --nologo -v quiet 2>&1 | tail -3
  ```

  Expected: `0 Error(s)`

- [ ] **Step 5: Smoke test — send + history**

  Start the app (connection not required for this test):

  ```bash
  dotnet run --project Desktop/Genie4.Desktop.csproj
  ```

  | # | Action | Expected |
  |---|--------|----------|
  | 1 | Type `hello world` → Enter | Command box clears (or selects-all if KeepInput enabled) |
  | 2 | Type `go south` → Enter | Same |
  | 3 | Press ↑ | `go south` appears |
  | 4 | Press ↑ again | `hello world` appears |
  | 5 | Press ↓ | `go south` appears |
  | 6 | Press ↓ again | Original pending input restored (empty string) |
  | 7 | Type `hi` → Enter | Not saved to history (< 3 chars) — ↑ still shows `go south` |
  | 8 | Type `hello world` → Enter | Not duplicated — ↑ shows `go south` (not `hello world` twice) |
  | 9 | Type `new command` → Enter | Saved |
  | 10 | Ctrl+Enter | `new command` re-sent immediately |

- [ ] **Step 6: Smoke test — tab completion (alias context)**

  (Requires the game to have at least one alias loaded, e.g. from a profile file.)

  | # | Action | Expected |
  |---|--------|----------|
  | 1 | Type partial alias text → Tab | Cycles to first matching alias |
  | 2 | Tab again | Cycles to next match; wraps after last |
  | 3 | Type any letter mid-cycle | Tab state resets; next Tab restarts from current text |
  | 4 | Type text with trailing space → Tab | Nothing happens |

- [ ] **Step 7: Smoke test — tab completion (script context)**

  (Requires `Config.ScriptDir` to exist with `.cmd` or similar files.)

  Type `.` + partial script name → Tab. Expected: cycles through matching script names (no extension).

- [ ] **Step 8: Smoke test — smart paste**

  | # | Action | Expected |
  |---|--------|----------|
  | 1 | Copy ≤100 chars → Ctrl+V | Inserted at caret; no dialog |
  | 2 | Copy 101+ chars → Ctrl+V | Confirmation dialog appears |
  | 3 | In dialog: click No | Nothing pasted |
  | 4 | Ctrl+V again → click Yes | Text inserted; CRLF stripped |
  | 5 | Copy text with newlines → Ctrl+V | Newlines removed; single line inserted |

- [ ] **Step 9: Smoke test — PageUp/Down scrolling**

  Connect (or paste enough output to overflow the window). With focus in the command box:

  | Key | Expected |
  |-----|----------|
  | PageUp | Output scrolls up one viewport height |
  | PageDown | Output scrolls down one viewport height |
  | Ctrl+PageUp | Output jumps to the very top |
  | Ctrl+PageDown | Output jumps to the very bottom |

- [ ] **Step 10: Commit**

  ```bash
  git add Desktop/CommandInputController.cs Desktop/MainWindow.axaml.cs
  git commit -m "feat: add CommandInputController with history, tab completion, smart paste, scrolling"
  ```

---

*End of plan.*
