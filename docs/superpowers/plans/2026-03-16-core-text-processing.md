# Core Text Processing Integration Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Avalonia client to feature parity with the WinForms client by adding the startup config-load sequence, the 10ms game loop timer (script ticks, command queue, event queue), and wiring all remaining Game and Command events.

**Architecture:** `MainWindow` owns `_command` (Command instance) and `_scriptList` (ScriptList), mirrors FormMain directly. An Avalonia `DispatcherTimer` at 10ms drives the game loop. Event wiring is split into Game events (13 remaining) and Command events (active + stubs).

**Tech Stack:** Avalonia 11, .NET 10, C#, `GenieClient.Genie` (Game, Globals, Command), `GenieClient` (Script, ScriptList, LocalDirectory), `Avalonia.Threading.DispatcherTimer`

**Spec:** `docs/superpowers/specs/2026-03-16-core-text-processing-integration-design.md`

---

## Key Reference: Exact Event Signatures

Read these before implementing — they differ from what the spec assumed.

**Game events (from `Core/Game.cs`):**
```csharp
event EventClearWindowEventHandler       EventClearWindow;     // delegate(string sWindow)
event EventDataRecieveEndEventHandler    EventDataRecieveEnd;  // delegate()  ← note misspelling
event EventRoundTimeEventHandler         EventRoundTime;       // delegate(int time)
event EventCastTimeEventHandler          EventCastTime;        // delegate()
event EventSpellTimeEventHandler         EventSpellTime;       // delegate()
event EventClearSpellTimeEventHandler    EventClearSpellTime;  // delegate()
event EventTriggerParseEventHandler      EventTriggerParse;    // delegate(string text)
event EventTriggerMoveEventHandler       EventTriggerMove;     // delegate()
event EventTriggerPromptEventHandler     EventTriggerPrompt;   // delegate()
event EventStatusBarUpdateEventHandler   EventStatusBarUpdate; // delegate()
event EventParseXMLEventHandler          EventParseXML;        // delegate(string xml)
event EventStreamWindowEventHandler      EventStreamWindow;    // delegate(object sID, object sTitle, object sIfClosed)
event EventAddImageEventHandler          EventAddImage;        // delegate(string filename, string window, int width, int height)
```

**Command events (from `Core/Command.cs`):**
```csharp
event EventEchoTextEventHandler          EventEchoText;        // delegate(string sText, string sWindow)
event EventEchoColorTextEventHandler     EventEchoColorText;   // delegate(string sText, Color oColor, Color oBgColor, string sWindow)
event EventLinkTextEventHandler          EventLinkText;        // delegate(string sText, string sLink, string sWindow)
event EventSendTextEventHandler          EventSendText;        // delegate(string sText, bool bUserInput, string sOrigin)
event EventSendRawEventHandler           EventSendRaw;         // delegate(string sText)
event EventParseLineEventHandler         EventParseLine;       // delegate(string sText)
event EventRunScriptEventHandler         EventRunScript;       // delegate(string sText)
event EventClearWindowEventHandler       EventClearWindow;     // delegate(string sWindow)
event EventVariableChangedEventHandler   EventVariableChanged; // delegate(string sVariable)
event EventChangeWindowTitleEventHandler EventChangeWindowTitle; // delegate(string sWindow, string sComment)
event EventConnectEventHandler           EventConnect;         // delegate(string sAccountName, string sPassword, string sCharacter, string sGame, bool isLich)
event EventDisconnectEventHandler        EventDisconnect;      // delegate()
event EventReconnectEventHandler         EventReconnect;       // delegate()
event EventExitEventHandler              EventExit;            // delegate()
event EventPresetChangedEventHandler     EventPresetChanged;   // delegate(string sPreset) — stub
event EventListScriptsEventHandler       EventListScripts;     // delegate(string sFilter) — stub
event EventScriptTraceEventHandler       EventScriptTrace;     // delegate(string sScript) — stub
event EventScriptAbortEventHandler       EventScriptAbort;     // delegate(string sScript) — stub
event EventScriptPauseEventHandler       EventScriptPause;     // delegate(string sScript) — stub
event EventScriptPauseOrResumeEventHandler EventScriptPauseOrResume; // delegate(string sScript) — stub
event EventScriptResumeEventHandler      EventScriptReload;    // delegate(string sScript) — stub
event EventScriptResumeEventHandler      EventScriptResume;    // delegate(string sScript) — stub
event EventScriptVariablesEventHandler   EventScriptVariables; // delegate(string sScript, string sFilter) — stub
event EventAddImageHandler               EventAddImage;        // delegate(string, string, int, int) — stub
event ListPluginsEventHandler            ListPlugins;          // delegate() — plugin phase stub
event LoadPluginEventHandler             LoadPlugin;           // delegate(string filename) — plugin phase stub
event UnloadPluginEventHandler           UnloadPlugin;         // delegate(string filename) — plugin phase stub
event ReloadPluginsEventHandler          ReloadPlugins;        // delegate() — plugin phase stub
event DisablePluginEventHandler          DisablePlugin;        // delegate(string filename) — plugin phase stub
event EnablePluginEventHandler           EnablePlugin;         // delegate(string filename) — plugin phase stub
```

**Script events (from `Script/Script.cs`):**
```csharp
event EventPrintErrorEventHandler  EventPrintError;  // delegate(string sText)
event EventPrintTextEventHandler   EventPrintText;   // delegate(string sText, Color oColor, Color oBgColor)
event EventSendTextEventHandler    EventSendText;    // delegate(string Text, string Script, bool ToQueue, bool DoCommand)
event EventStatusChangedEventHandler EventStatusChanged; // delegate(Script sender, ScriptState state) — stub
event EventDebugChangedEventHandler  EventDebugChanged;  // delegate(Script sender, int iLevel) — stub
```

**Script trigger methods:**
```csharp
void TriggerParse(string text, bool bBufferWait = true)
void TriggerMove()
void TriggerPrompt()
void AbortScript()
bool LoadFile(string sScriptName, ArrayList oArgList)
void RunScript()
bool ScriptDone { get; }
string FileName { get; }
```

**Command.ParseCommand signature:**
```csharp
public async Task<string> ParseCommand(string sText, bool bSendToGame = false, bool bUserInput = false, string sOrigin = "", bool bParseQuickSend = true)
```
Call without `await` for fire-and-forget: `_ = _command.ParseCommand(sAction, true, false, "Trigger");`

---

## Chunk 1: DockManager and GameOutputPanel additions

### Task 1: Add ClearPanel, EnsureVisible, ClearOutput

**Files:**
- Modify: `Desktop/GameOutputPanel.axaml.cs`
- Modify: `Desktop/DockManager.cs`

- [ ] **Step 1: Add `ClearOutput()` to `GameOutputPanel.axaml.cs`**

  Add after the `OutputScroll` property (around line 32):
  ```csharp
  public void ClearOutput() => OutputText.Inlines?.Clear();
  ```

- [ ] **Step 2: Add `ClearPanel(string)` to `DockManager.cs`**

  > **Spec deviation note:** The design spec incorrectly assumed `EventClearWindow` passes a `Game.WindowTarget` enum. The actual delegate in `Core/Game.cs` is `delegate void EventClearWindowEventHandler(string sWindow)` — it passes a **string window name**. `ClearPanel` therefore takes a `string`, not a `WindowTarget`. The `TargetMap` lookup is not needed here because `EventClearWindow` already resolves the name on the server side.

  Add as a new public method:
  ```csharp
  public void ClearPanel(string sWindow)
  {
      var name = string.IsNullOrWhiteSpace(sWindow) ? "main" : sWindow.ToLower();
      if (_panels.TryGetValue(name, out var panel))
          panel.ClearOutput();
  }
  ```

- [ ] **Step 3: Add `EnsureVisible(string)` to `DockManager.cs`**

  Add adjacent to `ClearPanel`. Creates the panel if it doesn't exist and makes it visible:
  ```csharp
  public void EnsureVisible(string name)
  {
      if (string.IsNullOrWhiteSpace(name)) return;
      var panel = GetOrCreate(name.ToLower());
      if (panel.IsOutputHidden)
          SetVisible(panel, true);
  }
  ```

- [ ] **Step 4: Build**
  ```bash
  dotnet build /Users/bloy/src/Genie4/worktrees/core-text-processing/Desktop/Genie4.Desktop.csproj -nologo -v:q 2>&1 | grep ": error"
  ```
  Expected: `0 Error(s)`

- [ ] **Step 5: Commit**
  ```bash
  git add Desktop/GameOutputPanel.axaml.cs Desktop/DockManager.cs
  git commit -m "feat: add ClearPanel, EnsureVisible (DockManager) and ClearOutput (GameOutputPanel)"
  ```

---

## Chunk 2: Init sequence, script management, and game loop timer

### Task 2: Init sequence and game loop timer in MainWindow

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

**Context:** `MainWindow` needs three new additions:
1. `private Command? _command;` and `private ScriptList _scriptList = new();` and `private ScriptList _scriptListNew = new();` fields
2. `Opened` handler → calls `InitializeAsync()`
3. A `DispatcherTimer` at 10ms that mirrors `TimerBgWorker_Tick` in FormMain

**New using directives needed:**
```csharp
using Avalonia.Threading;
using GenieClient;           // Script, LocalDirectory
```
Note: `Command` and `ScriptList` are in `GenieClient.Genie` (already imported via `using GenieClient.Genie;`). `Script` and `LocalDirectory` are in `GenieClient`.

- [ ] **Step 1: Add fields and `Opened` subscription**

  In `MainWindow.axaml.cs`, add new fields after the existing field declarations (around line 16):
  ```csharp
  private Command? _command;
  private readonly ScriptList _scriptList    = new ScriptList();
  private readonly ScriptList _scriptListNew = new ScriptList();
  private DispatcherTimer? _gameLoopTimer;
  ```

  In the constructor, after the `Closing` subscription (line 52), add:
  ```csharp
  Opened += OnWindowOpened;
  ```

- [ ] **Step 2: Add `OnWindowOpened` and `InitializeAsync`**

  Add these methods to `MainWindow.axaml.cs`:

  ```csharp
  private async void OnWindowOpened(object? sender, EventArgs e)
  {
      Opened -= OnWindowOpened; // fire once
      await InitializeAsync();
  }

  private async Task InitializeAsync()
  {
      AppendInit("Using Encoding: Unicode (UTF-8)\r\n");
      AppendInit($"Genie User Data Path: {LocalDirectory.Path}\r\n\r\n");

      // ConfigDir is read fresh inside each lambda so that if settings.cfg changes
      // the config path, all subsequent loads use the updated value.
      await RunLoad("Loading Settings...",         () => _game.Globals.Config.Load(_game.Globals.Config.ConfigDir + @"\settings.cfg"));
      await RunLoad("Loading Presets...",          () => _game.Globals.PresetList.Load(_game.Globals.Config.ConfigDir + @"\presets.cfg"));
      await RunLoad("Loading Global Variables...", () => _game.Globals.VariableList.Load(_game.Globals.Config.ConfigDir + @"\variables.cfg"));
      await RunLoad("Loading Highlights...",       () => _game.Globals.LoadHighlights(_game.Globals.Config.ConfigDir + @"\highlights.cfg"));
      await RunLoad("Loading Names...",            () => _game.Globals.NameList.Load(_game.Globals.Config.ConfigDir + @"\names.cfg"));
      await RunLoad("Loading Macros...",           () => _game.Globals.MacroList.Load(_game.Globals.Config.ConfigDir + @"\macros.cfg"));
      await RunLoad("Loading Aliases...",          () => _game.Globals.AliasList.Load(_game.Globals.Config.ConfigDir + @"\aliases.cfg"));
      await RunLoad("Loading Substitutes...",      () => _game.Globals.SubstituteList.Load(_game.Globals.Config.ConfigDir + @"\substitutes.cfg"));
      await RunLoad("Loading Gags...",             () => _game.Globals.GagList.Load(_game.Globals.Config.ConfigDir + @"\gags.cfg"));
      await RunLoad("Loading Triggers...",         () => _game.Globals.TriggerList.Load(_game.Globals.Config.ConfigDir + @"\triggers.cfg"));
      await RunLoad("Loading Classes...",          () => _game.Globals.ClassList.Load(_game.Globals.Config.ConfigDir + @"\classes.cfg"));

      WireGameEvents();
      WireCommandEvents();
      StartGameLoopTimer();
  }

  private async Task RunLoad(string label, Action load)
  {
      AppendInit(label);
      try
      {
          await Task.Run(load);
          AppendInit("OK\r\n");
      }
      catch
      {
          AppendInit("FAILED\r\n");
      }
  }

  private void AppendInit(string text) =>
      _dockManager.Route(Game.WindowTarget.Main, string.Empty,
                         text, Color.WhiteSmoke, Color.Empty);
  ```

- [ ] **Step 3: Add `StartGameLoopTimer` and `OnGameLoopTick`**

  Mirrors `TimerBgWorker_Tick` from FormMain (10ms interval). Drives command queue, event queue, script ticks, and script list maintenance:

  ```csharp
  private void StartGameLoopTimer()
  {
      // Use explicit form (set Interval, wire Tick, then Start) to avoid ambiguity
      // about whether the 3-arg constructor auto-starts in Avalonia 11.
      _gameLoopTimer = new DispatcherTimer(DispatcherPriority.Background)
      {
          Interval = TimeSpan.FromMilliseconds(10)
      };
      _gameLoopTimer.Tick += OnGameLoopTick;
      _gameLoopTimer.Start();
  }

  private void OnGameLoopTick(object? sender, EventArgs e)
  {
      // Poll event queue (custom Genie events, e.g. from scripts)
      var evtAction = _game.Globals.Events.Poll();
      if (!string.IsNullOrEmpty(evtAction))
          _ = _command?.ParseCommand(evtAction, false, false, "Event");

      // Poll command queue (script-queued commands with timing)
      var webbed  = _game.Globals.VariableList["webbed"]?.ToString()  == "1";
      var stunned = _game.Globals.VariableList["stunned"]?.ToString() == "1";
      string queueCmd = _game.Globals.CommandQueue.Poll(HasRoundTime(), webbed, stunned);
      while (!string.IsNullOrEmpty(queueCmd))
      {
          _ = _command?.ParseCommand(queueCmd, true, false, "Queue");
          queueCmd = _game.Globals.CommandQueue.Poll(HasRoundTime(), webbed, stunned);
      }

      // Tick all running scripts
      TickScripts();

      // Move newly-created scripts into the active list
      SafeAddScripts();

      // Remove scripts that have finished
      SafeRemoveExitedScripts();
  }

  private bool HasRoundTime() =>
      DateTime.Now < _game.Globals.RoundTimeEnd;  // mirrors FormMain HasRoundTime property
  ```

- [ ] **Step 4: Add script list helpers**

  ```csharp
  private void TickScripts()
  {
      if (!_scriptList.AcquireReaderLock()) return;
      try
      {
          foreach (Script oScript in _scriptList)
              oScript.TickScript();   // method name is TickScript(), not Tick()
      }
      finally { _scriptList.ReleaseReaderLock(); }
  }

  // Mirrors FormMain.AddScripts() — lock order is _scriptList outer, _scriptListNew inner.
  private void SafeAddScripts()
  {
      if (_scriptListNew.Count == 0) return;
      if (!_scriptList.AcquireWriterLock()) return;
      try
      {
          if (_scriptListNew.AcquireWriterLock())
          {
              try
              {
                  foreach (Script s in _scriptListNew)
                      if (s != null) _scriptList.Add(s);
                  _scriptListNew.Clear();
              }
              finally { _scriptListNew.ReleaseWriterLock(); }
          }
          // else: unable to acquire inner lock — skip this tick
      }
      finally { _scriptList.ReleaseWriterLock(); }
  }

  // Note: there is a benign race between reader release and writer acquire where
  // indices could become stale if scripts are added concurrently. FormMain has
  // the same race — this is not a regression.
  private void SafeRemoveExitedScripts()
  {
      if (!_scriptList.AcquireReaderLock()) return;
      var removeList = new List<int>();
      try
      {
          for (int i = 0; i < _scriptList.Count; i++)
              if (_scriptList[i].ScriptDone) removeList.Add(i);
      }
      finally { _scriptList.ReleaseReaderLock(); }

      if (removeList.Count == 0) return;

      if (_scriptList.AcquireWriterLock())
      {
          try { for (int i = removeList.Count - 1; i >= 0; i--) _scriptList.RemoveAt(removeList[i]); }
          finally { _scriptList.ReleaseWriterLock(); }
      }
  }
  ```


- [ ] **Step 5: Stop timer on close**

  In the constructor, update the `Closing` handler:
  ```csharp
  Closing += (_, _) =>
  {
      _gameLoopTimer?.Stop();
      _dockManager.SaveDefaultLayout();
  };
  ```

- [ ] **Step 6: Build**
  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj -nologo -v:q 2>&1 | grep ": error"
  ```
  Expected: `0 Error(s)`. If `Script.Tick()` doesn't exist, find the correct method name and update.

- [ ] **Step 7: Commit**
  ```bash
  git add Desktop/MainWindow.axaml.cs
  git commit -m "feat: add InitializeAsync, config load sequence, and 10ms game loop timer"
  ```

---

## Chunk 3: Game event and Command event wiring

### Task 3: Wire 13 remaining Game events

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

Add `WireGameEvents()` method (called from `InitializeAsync` after loads complete). All active handlers dispatch to the UI thread; stubs are empty lambdas with no-op.

- [ ] **Step 1: Add `WireGameEvents()`**

  ```csharp
  private void WireGameEvents()
  {
      // ── Active handlers ───────────────────────────────────────────────────────

      // Clear a named output panel (window name string, not WindowTarget enum)
      _game.EventClearWindow += (sWindow) =>
          Dispatcher.UIThread.Post(() => _dockManager.ClearPanel(sWindow));

      // Create/show a named sub-window on demand from game XML
      // Skip "main" — it always exists. Cast objects to string.
      _game.EventStreamWindow += (sID, sTitle, sIfClosed) =>
      {
          var name = sID?.ToString() ?? string.Empty;
          if (string.Equals(name, "main", StringComparison.OrdinalIgnoreCase)) return;
          Dispatcher.UIThread.Post(() => _dockManager.EnsureVisible(name));
      };

      // Text line from server — run through triggers and notify scripts
      _game.EventTriggerParse += (sText) =>
          Task.Run(() => ParseTriggers(sText));

      // Server prompt received — notify all running scripts
      _game.EventTriggerPrompt += () =>
      {
          if (!_scriptList.AcquireReaderLock()) return;
          try { foreach (Script s in _scriptList) s.TriggerPrompt(); }
          finally { _scriptList.ReleaseReaderLock(); }
      };

      // Movement detected — notify all running scripts
      _game.EventTriggerMove += () =>
      {
          if (!_scriptList.AcquireReaderLock()) return;
          try { foreach (Script s in _scriptList) s.TriggerMove(); }
          finally { _scriptList.ReleaseReaderLock(); }
      };

      // ── Stubs (subscribed now, UI deferred to later sub-projects) ─────────────
      _game.EventDataRecieveEnd  += () => { /* TODO: end-of-update flush */ };
      _game.EventRoundTime       += (_) => { /* TODO: roundtime countdown bar */ };
      _game.EventCastTime        += ()  => { /* TODO: cast timer bar */ };
      _game.EventSpellTime       += ()  => { /* TODO: active spell timer */ };
      _game.EventClearSpellTime  += ()  => { /* TODO: clear spell timer display */ };
      _game.EventStatusBarUpdate += ()  => { /* TODO: vitals bars */ };
      _game.EventParseXML        += (_) => { /* TODO: plugin XML forwarding */ };
      _game.EventAddImage        += (filename, window, w, h) => { /* TODO: inline images */ };
  }
  ```

- [ ] **Step 2: Add `ParseTriggers(string sText)` helper**

  Mirrors FormMain's `ParseTriggers`. Checks `_triggersEnabled`, iterates `TriggerList` for regex matches, then notifies active scripts. Called from a background thread (via `Task.Run`):

  ```csharp
  private void ParseTriggers(string sText, bool bBufferWait = true)
  {
      if (!_triggersEnabled) return;
      if (string.IsNullOrWhiteSpace(sText)) return;

      // Trigger list — regex match → ParseCommand
      if (_game.Globals.TriggerList.AcquireReaderLock())
      {
          try
          {
              foreach (Globals.Triggers.Trigger oTrigger in _game.Globals.TriggerList.Values)
              {
                  if (!oTrigger.IsActive || oTrigger.bIsEvalTrigger) continue;
                  if (oTrigger.oRegexTrigger == null) continue;

                  var match = oTrigger.oRegexTrigger.Match(sText);
                  if (!match.Success) continue;

                  var args = new System.Collections.ArrayList();
                  for (int j = 1; j < match.Groups.Count; j++)
                      args.Add(match.Groups[j].Value);

                  // Substitute $1..$N into action string
                  var action = oTrigger.sAction;
                  for (int i = 0; i < _game.Globals.Config.iArgumentCount; i++)
                      action = action.Replace("$" + (i + 1),
                          i < args.Count ? args[i].ToString().Replace("\"", "") : string.Empty);
                  if (args.Count > 0)
                      action = action.Replace("$0", args[0].ToString().Replace("\"", ""));
                  else
                      action = action.Replace("$0", string.Empty);

                  _ = _command?.ParseCommand(action, true, false, "Trigger");
              }
          }
          finally { _game.Globals.TriggerList.ReleaseReaderLock(); }
      }

      // Script list — notify each running script
      if (_scriptList.AcquireReaderLock())
      {
          try { foreach (Script s in _scriptList) s.TriggerParse(sText, bBufferWait); }
          finally { _scriptList.ReleaseReaderLock(); }
      }
  }
  ```

  > **Note:** The `Globals.Triggers.Trigger` type — verify exact namespace by checking:
  > ```bash
  > grep -n "class Trigger\b\|namespace" Lists/Globals.cs
  > ```
  > Adjust the `foreach` cast type if the namespace path differs.

- [ ] **Step 3: Build**
  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj -nologo -v:q 2>&1 | grep ": error"
  ```
  Expected: `0 Error(s)`.

- [ ] **Step 4: Commit**
  ```bash
  git add Desktop/MainWindow.axaml.cs
  git commit -m "feat: wire 13 remaining Game events (ClearWindow, StreamWindow, triggers, stubs)"
  ```

---

### Task 4: Create Command and wire Command events

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

Add `WireCommandEvents()` method (called from `InitializeAsync` after `WireGameEvents()`). Creates the `Command` instance, then subscribes to all its events.

- [ ] **Step 1: Add `WireCommandEvents()`**

  ```csharp
  private void WireCommandEvents()
  {
      // Game.Globals is a read-only property; cannot pass by ref directly.
      var globals = _game.Globals;
      _command = new Command(ref globals);

      // ── Text output ──────────────────────────────────────────────────────────

      // Script/command echo to a named window (Color.WhiteSmoke, no BG)
      _command.EventEchoText += (sText, sWindow) =>
          Dispatcher.UIThread.Post(() => RouteCommandText(sText, sWindow, Color.WhiteSmoke, Color.Empty));

      // Script echo with explicit color
      _command.EventEchoColorText += (sText, oColor, oBgColor, sWindow) =>
          Dispatcher.UIThread.Post(() => RouteCommandText(sText, sWindow, oColor, oBgColor));

      // Link text — treat same as EchoText for now (link behavior deferred)
      _command.EventLinkText += (sText, sLink, sWindow) =>
          Dispatcher.UIThread.Post(() => RouteCommandText(sText, sWindow, Color.WhiteSmoke, Color.Empty));

      // ── Network ──────────────────────────────────────────────────────────────

      // Send raw bytes to game socket
      _command.EventSendRaw += (sText) =>
          _game.SendRaw(sText);

      // Send processed text to game (with optional trigger-on-input)
      _command.EventSendText += (sText, bUserInput, sOrigin) =>
      {
          _game.SendText(sText, bUserInput, sOrigin);
          if (_game.Globals.Config.bTriggerOnInput)
              Task.Run(() => ParseTriggers(sText));
      };

      // ── Parsing and scripts ──────────────────────────────────────────────────

      // Script-generated text line → run through triggers
      _command.EventParseLine += (sText) =>
      {
          if (!string.IsNullOrWhiteSpace(sText))
              Task.Run(() => ParseTriggers(sText, false));
      };

      // Run a named script file
      _command.EventRunScript += (sText) =>
          Task.Run(() => LoadAndRunScript(sText));

      // ── Window management ────────────────────────────────────────────────────

      // Command clears a named window (same handler as game EventClearWindow)
      _command.EventClearWindow += (sWindow) =>
          Dispatcher.UIThread.Post(() => _dockManager.ClearPanel(sWindow));

      // Command or script changes the window title
      _command.EventChangeWindowTitle += (sWindow, sComment) =>
          Dispatcher.UIThread.Post(() => UpdateWindowTitle());

      // ── Connection lifecycle ─────────────────────────────────────────────────

      _command.EventConnect    += (account, password, character, game, isLich) =>
          Task.Run(() => _game.Connect(string.Empty, account, password, character, game));
      _command.EventDisconnect += () => _game.Disconnect();
      _command.EventReconnect  += () => { /* TODO: reconnect — call Disconnect() then Connect() with saved profile; Game has no Reconnect() method */ };
      _command.EventExit       += () => Dispatcher.UIThread.Post(Close);

      // ── Variable changes (Command also fires this) ───────────────────────────
      _command.EventVariableChanged += OnVariableChanged;

      // ── Script UI (stubs — script panel UI deferred) ─────────────────────────
      _command.EventListScripts       += (_)    => { /* TODO: list scripts panel */ };
      _command.EventScriptTrace       += (_)    => { /* TODO: script trace panel */ };
      _command.EventScriptAbort       += (_)    => { /* TODO: script abort UI */ };
      _command.EventScriptPause       += (_)    => { /* TODO: script pause UI */ };
      _command.EventScriptPauseOrResume += (_)  => { /* TODO: script pause/resume UI */ };
      _command.EventScriptReload      += (_)    => { /* TODO: script reload UI */ };
      _command.EventScriptResume      += (_)    => { /* TODO: script resume UI */ };
      _command.EventScriptVariables   += (_, _) => { /* TODO: script variables panel */ };
      _command.EventPresetChanged     += (_)    => { /* TODO: reapply highlight colors */ };

      // ── Plugin lifecycle (plugin phase) ──────────────────────────────────────
      _command.ListPlugins  += ()    => { /* TODO: plugin phase */ };
      _command.LoadPlugin   += (_)   => { /* TODO: plugin phase */ };
      _command.UnloadPlugin += (_)   => { /* TODO: plugin phase */ };
      _command.ReloadPlugins += ()   => { /* TODO: plugin phase */ };
      _command.DisablePlugin += (_)  => { /* TODO: plugin phase */ };
      _command.EnablePlugin  += (_)  => { /* TODO: plugin phase */ };

      // ── Status bar / debug stubs ─────────────────────────────────────────────
      _command.EventStatusBar     += (_, _) => { /* TODO: status bar text */ };
      _command.EventScriptDebug   += (_, _) => { /* TODO: script debug output */ };

      // ── Image/misc stubs ─────────────────────────────────────────────────────
      _command.EventAddImage      += (f, w, wi, h) => { /* TODO: inline images */ };
  }
  ```

  > **Note on `_game.Globals` as `ref`:** `Command(ref Globals cl)` cannot take a property directly. `Game.Globals` is a read-only property (`public Globals Globals => m_oGlobals;`), so use a local variable — this is safe because Command stores the value, not a persistent ref:
  > ```csharp
  > var globals = _game.Globals;
  > _command = new Command(ref globals);
  > ```

- [ ] **Step 2: Add `RouteCommandText` helper**

  Routes script-echoed text to the right panel. Window name `""`, `"game"`, or `"main"` all go to main:

  ```csharp
  private void RouteCommandText(string sText, string sWindow, Color oColor, Color oBgColor)
  {
      bool isMono = sText.StartsWith("mono ", StringComparison.OrdinalIgnoreCase);
      if (isMono) sText = sText[5..];

      if (string.IsNullOrEmpty(sWindow)
          || sWindow.Equals("game", StringComparison.OrdinalIgnoreCase)
          || sWindow.Equals("main", StringComparison.OrdinalIgnoreCase))
      {
          _dockManager.Route(Game.WindowTarget.Main, string.Empty, sText, oColor, oBgColor);
      }
      else
      {
          _dockManager.Route(Game.WindowTarget.Other, sWindow, sText, oColor, oBgColor);
      }
  }
  ```

- [ ] **Step 3: Add `LoadAndRunScript` helper**

  Mirrors FormMain's `ClassCommand_RunScript` + `LoadScript`. Runs on a background thread:

  ```csharp
  private void LoadAndRunScript(string sText)
  {
      var al = Utility.ParseArgs(sText, true);
      if (al.Count == 0) return;

      string scriptName = al[0].ToString()!.ToLower().Trim().TrimStart('#');
      if (!scriptName.EndsWith($".{_game.Globals.Config.ScriptExtension}"))
          scriptName += $"." + _game.Globals.Config.ScriptExtension;

      // Abort duplicate if configured
      if (_game.Globals.Config.bAbortDupeScript && _scriptList.AcquireReaderLock())
      {
          try
          {
              foreach (Script existing in _scriptList)
                  if (existing.FileName == scriptName) existing.AbortScript();
          }
          finally { _scriptList.ReleaseReaderLock(); }
      }

      var oScript = new Script(_game.Globals);
      oScript.EventPrintError  += (sErr)           => Dispatcher.UIThread.Post(() =>
          _dockManager.Route(Game.WindowTarget.Main, string.Empty, sErr, Color.WhiteSmoke, Color.DarkRed));
      oScript.EventPrintText   += (sTxt, clr, bg)  => Dispatcher.UIThread.Post(() =>
          _dockManager.Route(Game.WindowTarget.Main, string.Empty, sTxt, clr, bg));
      oScript.EventSendText    += (text, script, toQueue, doCommand) =>
          Task.Run(() => HandleScriptSendText(text, script, toQueue, doCommand));
      oScript.EventStatusChanged += (_, _) => { /* TODO: script status toolbar */ };
      oScript.EventDebugChanged  += (_, _) => { /* TODO: script debug UI */ };

      if (!oScript.LoadFile(scriptName, al)) return;

      if (_scriptListNew.AcquireWriterLock())
      {
          try { _scriptListNew.Add(oScript); }
          finally { _scriptListNew.ReleaseWriterLock(); }
      }
      oScript.RunScript();
  }

  private void HandleScriptSendText(string text, string script, bool toQueue, bool doCommand)
  {
      bool sendToGame = !text.StartsWith(_game.Globals.Config.cCommandChar.ToString());
      if (!toQueue)
      {
          _ = _command?.ParseCommand(text, sendToGame, false, script);
      }
      else
      {
          string sNumber = string.Empty;
          foreach (char c in text)
          {
              if (char.IsDigit(c) || c == '.') sNumber += c;
              else break;
          }
          double delay = sNumber.Length > 0 ? double.Parse(sNumber) : 0;
          string action = _game.Globals.ParseGlobalVars(
              sNumber.Length > 0 ? text[sNumber.Length..].Trim() : text);
          _game.Globals.CommandQueue.AddToQueue(delay, action, true, doCommand, doCommand);
      }
  }
  ```

  > **Note on `Script` constructor:** FormMain passes `var argcl = m_oGlobals; new Script(argcl)`. Check the Script constructor signature:
  > ```bash
  > grep -n "public Script(" Script/Script.cs | head -3
  > ```
  > Match the constructor call to what Script actually accepts.

- [ ] **Step 4: Build**
  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj -nologo -v:q 2>&1 | grep ": error"
  ```
  Expected: `0 Error(s)`. Fix any type/namespace issues found (the notes above highlight likely trouble spots).

- [ ] **Step 5: Commit**
  ```bash
  git add Desktop/MainWindow.axaml.cs
  git commit -m "feat: create Command, wire Command events, add script load/run and trigger helpers"
  ```

---

## Smoke Test (manual)

Run the app and verify:

- [ ] Startup prints the full init sequence to the main panel:
  ```
  Using Encoding: Unicode (UTF-8)
  Genie User Data Path: <path>

  Loading Settings...OK
  Loading Presets...OK
  ...
  Loading Classes...OK
  ```
- [ ] If a config file is missing, that step prints `FAILED` and loading continues
- [ ] Connect to game — game text appears in the main panel
- [ ] Type an alias in the command box — it expands (aliases loaded from config)
- [ ] A trigger fires when matching server text arrives (trigger command executes)
- [ ] Type a script command (e.g. `#pause 1`) — no crash
- [ ] `EventClearWindow` clears the correct panel (verify via a game command that triggers it)
- [ ] `EventStreamWindow` with a new window name shows a new panel in the dock

