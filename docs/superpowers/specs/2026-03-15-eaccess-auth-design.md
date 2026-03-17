---
name: EACCESS Authentication — Sub-project 2 of N
description: Design spec for wiring full Simutronics EACCESS account authentication into the Avalonia Desktop UI, replacing the DirectConnect placeholder.
type: project
---

# EACCESS Authentication Design

**Date:** 2026-03-15
**Branch:** `install-mono-for-mac-linux`
**Sub-project:** 2 of N (Authentication wiring)

## Background

The Avalonia Desktop project (sub-project 1) ships with a `DirectConnect` placeholder in `ConnectButton_Click`. `DirectConnect` bypasses the Simutronics EACCESS authentication server and connects raw to the game socket — the game server rejects this unless a pre-obtained session key is passed. The account and password fields collected by the UI were intentionally left unused pending this sub-project.

`Game.Connect(sGenieKey, sAccountName, sPassword, sCharacter, sGame)` implements the full EACCESS flow in the existing shared `Core/Game.cs`:
1. Opens a TLS connection to `eaccess.play.net:7910`
2. Receives a 32-byte encryption key; sends encrypted account + password
3. Receives a session key (`m_sLoginKey`) and account owner; requests the target game
4. Receives the character list; looks up the pre-specified character key; sends `L` command
5. Receives game host, port, and connect key; disconnects from EACCESS
6. Connects directly to the game server with the session key
7. Completes the game handshake

Auth errors (bad password, no account, no such character) flow through `EventPrintText` / `EventPrintError` to the output area automatically. No changes to the auth state machine are needed.

## Scope

**This sub-project covers:**
- Wire `Game.Connect()` into `ConnectButton_Click` in `Desktop/MainWindow.axaml.cs`
- Add `public event Action? EventDisconnected` to `Core/Game.cs` so the Desktop UI can re-enable the Connect button on any disconnect
- Disable Connect button on click; re-enable on disconnect

**Explicitly out of scope:**
- Character selection UI (user types character name upfront, matching the original app)
- Reconnect logic (auto-reconnect is already implemented in `Game.cs`; the button re-enable on disconnect handles the "reconnect manually" case)
- Saving credentials (deferred)
- A "Disconnect" button (deferred — user closes the window or the game session ends)

## Files Changed

| File | Change |
|------|--------|
| `Core/Game.cs` | Add `public event Action? EventDisconnected;` (1 line); fire it in `GameSocket_EventDisconnected()` (1 line) |
| `Desktop/MainWindow.axaml.cs` | Swap `DirectConnect` → `Connect`; validate all three fields; disable/re-enable `ConnectButton` |

`Desktop/MainWindow.axaml` — **no changes**.

## Core/Game.cs — EventDisconnected

Add after the last public event declaration (after `EventStreamWindow` at line 90):

```csharp
public event Action? EventDisconnected;
```

In `GameSocket_EventDisconnected()` (line 3208), add as the first line:

```csharp
EventDisconnected?.Invoke();
```

This fires for every socket disconnect regardless of `ConnectStates` — including EACCESS auth failures (which disconnect in `ConnectedKey` state, not `ConnectedGame`). The existing `$connected` variable change logic is preserved unchanged beneath it.

## Desktop/MainWindow.axaml.cs — ConnectButton_Click

Add `using System.Threading.Tasks;` to the top of the file (alongside the existing `using` directives).

Replace the current placeholder implementation with:

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

`sGenieKey` is passed as `string.Empty` — `Game.Connect()` ignores this parameter entirely and always calls `DoConnect`; passing empty string is conventional.

`Game.Connect()` → `DoConnect()` → `Connection.ConnectAndAuthenticate()` is synchronous (blocking TLS I/O). Calling it directly on the UI thread would freeze the window during the EACCESS handshake (which takes a network round-trip). `Task.Run` dispatches it to the thread pool, keeping the UI responsive. The `try/catch` ensures that any exception thrown before `EventDisconnected` fires (e.g., `SocketException` on DNS failure) is shown in the output area and re-enables the button rather than leaving it permanently disabled.

`EventPrintText` / `EventPrintError` already marshal output via `Dispatcher.UIThread.Post` in `OnPrintText`; the `OnDisconnected` handler specified below uses `Dispatcher.UIThread.Post` for the same reason, so no additional synchronization is needed for the normal auth flow.

## Desktop/MainWindow.axaml.cs — Constructor and Disconnect Handler

Add to the constructor:

```csharp
_game.EventDisconnected += OnDisconnected;
```

Add the handler:

```csharp
private void OnDisconnected()
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
}
```

## Success Criteria

1. Launching the app and entering valid account/password/character, then clicking Connect, successfully authenticates via EACCESS and reaches the game server — game text appears in the output area
2. Entering an incorrect password shows an error in the output area and re-enables the Connect button
3. Entering a non-existent account shows an error and re-enables the button
4. Leaving account or password blank shows a validation message and does not attempt a connection
5. The Connect button is disabled while connecting and re-enabled when the session ends (auth failure or game disconnect)
6. The existing Windows build (`dotnet build Genie4.csproj -f net6.0-windows`) is unaffected
