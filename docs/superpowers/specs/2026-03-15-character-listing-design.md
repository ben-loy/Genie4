---
name: Character Listing — Sub-project 3 of N
description: Design spec for wiring character listing into the Avalonia Desktop UI when no character name is supplied.
type: project
---

# Character Listing Design

**Date:** 2026-03-15
**Branch:** `install-mono-for-mac-linux`
**Sub-project:** 3 of N (Character listing)

## Background

The original Genie client supported a "list characters" mode: if the user launched the app without specifying a character name, it authenticated via EACCESS, printed the available character names to the output area, and exited. This allowed users to discover available characters before launching again with a specific name.

Sub-project 2 wired full EACCESS authentication into the Desktop UI but made the character field required, blocking this flow. The character listing path already exists in the shared `Core/Game.cs`:

- `ParseKeyRow` case `"C"` (line 1120): when `m_sAccountCharacter` is blank, prints `"Listing characters:"` then each character name via `PrintError`, then calls `m_oSocket.Disconnect()`.
- The disconnect fires `EventDisconnected`, which re-enables the Connect button (wired in sub-project 2).

The only missing pieces are:
1. The Desktop UI blocks the flow with a "Character name is required" validation guard.
2. `EventPrintError` is not subscribed in `MainWindow`, so the character list text is silently dropped.

## Scope

**This sub-project covers:**
- Remove the character-required validation guard from `ConnectButton_Click`
- Change the status message to `[Listing characters...]` when character field is blank
- Wire `EventPrintError` → `OnPrintError` in `MainWindow` so character names (and all other error-channel text) are visible

**Explicitly out of scope:**
- Character selection UI (clicking a name to connect — user types the name and clicks Connect again)
- Saving the character list between sessions
- Any changes to `Core/Game.cs` or `Connection.cs`

## Files Changed

| File | Change |
|------|--------|
| `Desktop/MainWindow.axaml.cs` | Remove character-required guard; update status message; add `EventPrintError` subscription and `OnPrintError` handler |

`Desktop/MainWindow.axaml` and `Core/Game.cs` — **no changes**.

## Desktop/MainWindow.axaml.cs — ConnectButton_Click

Remove the character-required block:

```csharp
// DELETE this block entirely:
if (string.IsNullOrWhiteSpace(character))
{
    AppendOutput("[Connect] Character name is required.");
    return;
}
```

Replace the single status line:

```csharp
// BEFORE:
AppendOutput($"[Connecting as {character}...]");

// AFTER:
AppendOutput(string.IsNullOrWhiteSpace(character)
    ? "[Listing characters...]"
    : $"[Connecting as {character}...]");
```

The resulting `ConnectButton_Click` validates only account and password (both required), then accepts an optional character name. Passing an empty string to `_game.Connect()` triggers the character listing path in `Game.cs` unchanged.

## Desktop/MainWindow.axaml.cs — EventPrintError

Add to the constructor (after `_game.EventDisconnected += OnDisconnected`):

```csharp
_game.EventPrintError += OnPrintError;
```

Add the handler (alongside `OnPrintText` and `OnDisconnected`):

```csharp
private void OnPrintError(string text)
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
}
```

`EventPrintErrorEventHandler` has the signature `(string text)` — simpler than `EventPrintText`. The handler is identical in structure to `OnPrintText` and `OnDisconnected`: marshal to the UI thread, then call `AppendOutput`.

## Flow

**Character listing (character field blank):**
1. User enters account + password, leaves Character blank, clicks Connect
2. Button disables; `[Listing characters...]` appears in output
3. `Task.Run` dispatches `_game.Connect(string.Empty, account, password, "", "DR")`
4. EACCESS authenticates; `Game.cs` case `"C"` detects blank `m_sAccountCharacter`
5. `PrintError("Listing characters:")` fires `EventPrintError` → `OnPrintError` → `AppendOutput`
6. Each character name fires `EventPrintError` → `OnPrintError` → `AppendOutput`
7. `m_oSocket.Disconnect()` fires `EventDisconnected` → `OnDisconnected` → button re-enables
8. User reads the list, types a character name, clicks Connect again

**Normal connect (character field populated):** unchanged from sub-project 2.

## Success Criteria

1. With account + password filled and Character blank: clicking Connect disables the button, shows `[Listing characters...]`, then shows the character names in the output area, then re-enables the button
2. With all three fields filled: behavior is unchanged from sub-project 2
3. With account or password blank: still shows `[Connect] Account and password are required.` and does not attempt a connection
4. The existing Windows build (`dotnet build Genie4.csproj -f net6.0-windows`) is unaffected
