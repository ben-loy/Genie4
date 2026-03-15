# EACCESS Authentication Wiring Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `DirectConnect` placeholder in the Avalonia Desktop UI with full Simutronics EACCESS account authentication.

**Architecture:** Two surgical edits — add a public `EventDisconnected` event to `Core/Game.cs` (fired on every socket disconnect) so the Desktop UI can re-enable its Connect button, then replace the `ConnectButton_Click` stub in `Desktop/MainWindow.axaml.cs` with validation, `Game.Connect()` dispatched on a thread-pool thread, and an `OnDisconnected` handler that re-enables the button via the UI dispatcher.

**Tech Stack:** C# / .NET 10.0 (Desktop target), Avalonia 11, `System.Threading.Tasks.Task.Run` for async dispatch, `Avalonia.Threading.Dispatcher.UIThread.Post` for cross-thread UI updates.

---

## Context for the implementer

### Repository layout

```
Genie4/
├── Core/
│   ├── Game.cs           ← shared game engine (net6.0-windows + DESKTOP)
│   └── Connection.cs     ← TLS socket wrapper
├── Desktop/
│   ├── Genie4.Desktop.csproj  ← net10.0 Avalonia target, defines DESKTOP constant
│   ├── MainWindow.axaml.cs    ← Avalonia code-behind — THE file we edit
│   └── App.axaml.cs
└── Genie4.csproj              ← net6.0-windows primary target (DO NOT BREAK)
```

### Preprocessor constants

`Desktop/Genie4.Desktop.csproj` defines `<DefineConstants>DESKTOP</DefineConstants>`. Any `#if !DESKTOP` blocks in shared files compile out in the Desktop build. `Core/Game.cs` is shared between both targets.

### Build commands

```bash
# Desktop (cross-platform, net10.0):
dotnet run --project Desktop/Genie4.Desktop.csproj

# Windows primary (must stay green — no runtime needed, just verify it compiles):
dotnet build Genie4.csproj -f net6.0-windows
```

All commands run from the repo root: `/Users/bloy/src/Genie4/worktrees/auth/`

### No automated test suite

This project has no unit test project. "Tests" are build verification + manual smoke tests described in each task.

---

## Chunk 1: EACCESS Authentication Wiring

### Task 1: Add `EventDisconnected` to `Core/Game.cs`

**Files:**
- Modify: `Core/Game.cs:90-92` (add event after `EventStreamWindow` delegate)
- Modify: `Core/Game.cs:3208` (fire event at top of `GameSocket_EventDisconnected`)

**Background:** `Game.cs` exposes public events that consumers (WinForms UI, Desktop UI) subscribe to. The socket's internal `EventDisconnected` is private; we need a public wrapper so `MainWindow` can re-enable the Connect button on any disconnect — including EACCESS auth failures, which disconnect while in `ConnectedKey` state (not `ConnectedGame`).

`GameSocket_EventDisconnected()` is already wired as `_m_oSocket.EventDisconnected += GameSocket_EventDisconnected` and fires for every socket disconnect. We add `EventDisconnected?.Invoke()` as the very first line so it fires before any existing state-conditional logic.

- [ ] **Step 1: Open `Core/Game.cs` and locate the event block**

  The last public event declaration is at line 90:
  ```csharp
  public event EventStreamWindowEventHandler EventStreamWindow;

  public delegate void EventStreamWindowEventHandler(object sID, object sTitle, object sIfClosed);
  ```
  The next line (94) is `private Connection _m_oSocket;`.

- [ ] **Step 2: Add `EventDisconnected` after the `EventStreamWindow` delegate (after line 92)**

  Insert after line 92 (blank line then the two new lines):
  ```csharp
  public event Action? EventDisconnected;
  ```
  The result should look like:
  ```csharp
  public event EventStreamWindowEventHandler EventStreamWindow;

  public delegate void EventStreamWindowEventHandler(object sID, object sTitle, object sIfClosed);

  public event Action? EventDisconnected;

  private Connection _m_oSocket;
  ```

- [ ] **Step 3: Locate `GameSocket_EventDisconnected()` (around line 3208)**

  It looks like:
  ```csharp
  private void GameSocket_EventDisconnected()
  {
      if (m_oConnectState == ConnectStates.ConnectedGame)
      {
  ```

- [ ] **Step 4: Add `EventDisconnected?.Invoke();` as the first line of the method body**

  Result:
  ```csharp
  private void GameSocket_EventDisconnected()
  {
      EventDisconnected?.Invoke();
      if (m_oConnectState == ConnectStates.ConnectedGame)
      {
  ```

  Do not change anything else in this method.

- [ ] **Step 5: Verify Desktop build compiles**

  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj
  ```
  Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 6: Verify Windows primary build still compiles**

  ```bash
  dotnet build Genie4.csproj -f net6.0-windows
  ```
  Expected: `Build succeeded.` with 0 errors. (`Action?` is available in net6.0.)

- [ ] **Step 7: Commit**

  ```bash
  git add Core/Game.cs
  git commit -m "feat: add public EventDisconnected to Game for Desktop UI button re-enable"
  ```

---

### Task 2: Wire `ConnectButton_Click` in `Desktop/MainWindow.axaml.cs`

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs` (add `using`, replace `ConnectButton_Click`, add `OnDisconnected`, wire constructor)

**Background:** The current `ConnectButton_Click` calls `_game.DirectConnect(character, "DR", DefaultHost, DefaultPort)` — a bypass that goes straight to the raw game socket. The server rejects it. We replace it with `Game.Connect()` which performs the full EACCESS TLS handshake. Because `Connection.ConnectAndAuthenticate()` is synchronous blocking I/O, we dispatch it on the thread pool with `Task.Run` to avoid freezing the Avalonia UI thread. We wrap the call in try/catch so any pre-disconnect exceptions (e.g., DNS failure) still re-enable the button.

The `DefaultHost` and `DefaultPort` constants become unused after this change — remove them.

- [ ] **Step 1: Add `using System.Threading.Tasks;` to the top of `Desktop/MainWindow.axaml.cs`**

  Current using block (lines 1–4):
  ```csharp
  using System.Drawing;
  using Avalonia.Controls;
  using Avalonia.Input;
  using GenieClient.Genie;
  ```
  Add the new using so it becomes:
  ```csharp
  using System.Drawing;
  using System.Threading.Tasks;
  using Avalonia.Controls;
  using Avalonia.Input;
  using GenieClient.Genie;
  ```

- [ ] **Step 2: Remove the now-unused `DefaultHost` and `DefaultPort` constants**

  Delete these two lines (currently lines 10–11):
  ```csharp
  private const string DefaultHost = "dr.simutronics.net";
  private const int DefaultPort = 11024;
  ```

- [ ] **Step 3: Replace the entire `ConnectButton_Click` method**

  Current method (lines 22–40):
  ```csharp
  private void ConnectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
      // TODO(sub-project 2): Replace DirectConnect with Game.Connect(genieKey, account, password, character, "DR")
      // which routes through eaccess.play.net for full Simutronics account authentication.
      // Account and password fields are UI placeholders — collected here for future use.
      var account = AccountBox.Text ?? string.Empty;    // reserved for future EACCESS auth
      var password = PasswordBox.Text ?? string.Empty;  // reserved for future EACCESS auth
      var character = CharacterBox.Text ?? string.Empty;
      _ = account; _ = password; // suppress unused-variable warnings until wired

      if (string.IsNullOrWhiteSpace(character))
      {
          AppendOutput("[Connect] Character name is required.");
          return;
      }

      AppendOutput($"[Connecting as {character} to {DefaultHost}:{DefaultPort}...]");
      _game.DirectConnect(character, "DR", DefaultHost, DefaultPort);
  }
  ```

  Replace with:
  ```csharp
  private void ConnectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
      var account   = AccountBox.Text   ?? string.Empty;
      var password  = PasswordBox.Text  ?? string.Empty;
      var character = CharacterBox.Text ?? string.Empty;

      if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
      {
          AppendOutput("[Connect] Account and password are required.");
          return;
      }
      if (string.IsNullOrWhiteSpace(character))
      {
          AppendOutput("[Connect] Character name is required.");
          return;
      }

      // Disable before dispatching — ensures the button is disabled before OnDisconnected
      // could possibly fire (prevents a re-enable/disable race on immediate failure).
      ConnectButton.IsEnabled = false;
      AppendOutput($"[Connecting as {character}...]");
      _ = Task.Run(() =>
      {
          try
          {
              _game.Connect(string.Empty, account, password, character, "DR");
          }
          catch (Exception ex)
          {
              Avalonia.Threading.Dispatcher.UIThread.Post(() =>
              {
                  AppendOutput($"[Connect error: {ex.Message}]");
                  ConnectButton.IsEnabled = true;
              });
          }
      });
  }
  ```

- [ ] **Step 4: Wire `EventDisconnected` in the constructor**

  Current constructor:
  ```csharp
  public MainWindow(Game game)
  {
      InitializeComponent();
      _game = game;
      _game.EventPrintText += OnPrintText;
  }
  ```

  Add the subscription as the last line:
  ```csharp
  public MainWindow(Game game)
  {
      InitializeComponent();
      _game = game;
      _game.EventPrintText += OnPrintText;
      _game.EventDisconnected += OnDisconnected;
  }
  ```

- [ ] **Step 5: Add the `OnDisconnected` handler**

  Add this method to the class (e.g., after `OnPrintText`):
  ```csharp
  private void OnDisconnected()
  {
      Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
  }
  ```

- [ ] **Step 6: Verify Desktop build compiles**

  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj
  ```
  Expected: `Build succeeded.` with 0 errors. No CS0219 (unused variable) warnings.

- [ ] **Step 7: Verify Windows primary build still compiles**

  ```bash
  dotnet build Genie4.csproj -f net6.0-windows
  ```
  Expected: `Build succeeded.` with 0 errors. (`MainWindow.axaml.cs` is Desktop-only, so it only compiles under the Desktop csproj — this verifies the shared `Core/Game.cs` change didn't break anything.)

- [ ] **Step 8: Smoke test — validation paths (no network needed)**

  ```bash
  dotnet run --project Desktop/Genie4.Desktop.csproj
  ```

  a. Leave Account blank, enter a password and character name. Click Connect.
     Expected: output shows `[Connect] Account and password are required.` Button stays enabled.

  b. Enter an account and password, leave Character blank. Click Connect.
     Expected: output shows `[Connect] Character name is required.` Button stays enabled.

  c. Enter account, password, and a bogus character name (e.g. `XXXXXX`). Click Connect.
     Expected: Connect button disables. After a few seconds, an error message appears in the output area (e.g. "Invalid character" routed via `EventPrintText`/`EventPrintError`) and the button re-enables.

- [ ] **Step 9: Commit**

  ```bash
  git add Desktop/MainWindow.axaml.cs
  git commit -m "feat: wire Game.Connect EACCESS auth into Desktop ConnectButton_Click"
  ```
