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

Mirrors `FormMain.cs` lines 2179–2190 exactly:

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

## Path Resolution

`LocalDirectory.CheckUserDirectory()` checks whether a `Config/` folder
exists alongside the binary (portable mode). If not found, it calls
`SetUserDataDirectory()`, which on Mac/Linux resolves to:

```
~/.config/Genie4.Desktop/
```

On Windows (net6.0-windows build, not affected by this change):

```
%APPDATA%\Genie4\
```

## Implementation

```csharp
public override async void OnFrameworkInitializationCompleted()
{
    // Mirror FormMain startup directory initialization
    LocalDirectory.CheckUserDirectory();
    string dataPath = LocalDirectory.Path;
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Profiles"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Layout"));
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "PluginKeys"));
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

## Side Effects

- `Log.cs`'s `Directory.CreateDirectory(sDirectory)` (added as a crash fix)
  becomes a no-op safety net rather than the primary creator. Both remain
  correct.
- No shared code (`LocalDirectory.cs`, `Log.cs`, etc.) is modified.

## Out of Scope

- Changing the user data path (e.g., to `~/Genie5`) — deferred.
- Config file loading/saving — separate sub-project.
- XDG base directory spec compliance — deferred.
