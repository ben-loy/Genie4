# Desktop Directory Initialization — Design Spec

**Date:** 2026-03-15
**Status:** Approved

## Problem

The WinForms `FormMain` creates the full app directory structure at startup
(Config, Logs, Scripts, etc.). The Avalonia Desktop build has no equivalent
initialization, so subdirectories are missing at first run, causing crashes
(e.g., `Log.LogText` throwing `DirectoryNotFoundException` when it tries to
write to a `Logs/` folder that does not exist).

## Goal

Mirror `FormMain`'s startup directory initialization in the Desktop entry
point so the Desktop build creates the same directory structure on first
launch, on any platform.

## Approach

Call `LocalDirectory.CheckUserDirectory()` and then `Directory.CreateDirectory`
for each subdirectory at the very start of
`App.OnFrameworkInitializationCompleted()`, before the DI host is built.
No changes to `LocalDirectory.cs` or any shared code.

## File Changed

**`Desktop/App.axaml.cs`** — `OnFrameworkInitializationCompleted()` gains a
startup init block as its first action.

## Directory Structure Created

Mirrors `FormMain.cs` lines 2179–2190 (`CreateGenieFolders` private method):

```
{LocalDirectory.Path}/
  Config/
  Config/Profiles/
  Config/Layout/
  Config/PluginKeys/
  Help/
  Icons/
  Logs/
  Scripts/
  Sounds/
  Plugins/
  Maps/
```

`Directory.CreateDirectory` is idempotent — safe to call on every launch.

### `Utility.MoveLayoutFiles()` — intentionally excluded

`FormMain.CreateGenieFolders()` calls `Utility.MoveLayoutFiles()` between
creating `Config/PluginKeys/` and `Help/`. This is a one-time migration
helper that moves `*.layout` files from `Config/` to `Config/Layout/`. It
uses `FileSystem.Dir()` (a VB.NET Windows-only API), and a fresh Desktop
install has no pre-existing layout files to migrate. It is intentionally
excluded from the Desktop init.

## Path Resolution

`LocalDirectory.CheckUserDirectory()` checks whether a `Config/` folder
exists alongside the binary (portable mode). If not found, it calls
`SetUserDataDirectory()`.

`LocalDirectory.SetUserDataDirectory()` has a `#if DESKTOP` branch that
uses `Assembly.GetExecutingAssembly().GetName().Name` (= `"Genie.Desktop"`)
instead of `Application.ProductName`. On Mac/Linux this resolves to:

```
~/.config/Genie.Desktop/
```

On Windows with the WinForms build (`Genie4.csproj`, `net6.0-windows`, `DESKTOP` not defined), `Application.ProductName` = `"Genie Client 4"`:

```
%APPDATA%\Genie Client 4\
```

This change does not affect the WinForms build.

## Implementation

```csharp
public override async void OnFrameworkInitializationCompleted()
{
    // Mirror FormMain.CreateGenieFolders() startup directory initialization
    LocalDirectory.CheckUserDirectory();
    string dataPath = LocalDirectory.Path;
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Profiles"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Layout"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "PluginKeys"));
    // Note: Utility.MoveLayoutFiles() is intentionally omitted — Windows-only migration aid
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Help"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Icons"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Logs"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Scripts"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Sounds"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Plugins"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Maps"));

    // ... existing host builder code ...
}
```

## Exception Handling

`Directory.CreateDirectory` can throw `UnauthorizedAccessException` if the
path is not writable. Since `OnFrameworkInitializationCompleted` is
`async void`, any unhandled exception will be routed to Avalonia's global
unhandled exception handler (which terminates the app with a crash dialog by
default). No try/catch is added around the init block — a failure to create
the data directory is a fatal startup error and should surface immediately.

## Side Effects

- `Log.cs`'s `Directory.CreateDirectory(sDirectory)` (added as a crash fix)
  becomes a no-op safety net rather than the primary creator. Both remain
  correct.
- No shared code (`LocalDirectory.cs`, `Log.cs`, etc.) is modified.

## Out of Scope

- Changing the user data path (e.g., to `~/Genie5`) — deferred.
- Config file loading/saving — separate sub-project.
- XDG base directory spec compliance — deferred.
