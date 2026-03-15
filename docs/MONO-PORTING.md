# Running Genie on Mac and Linux (Mono)

Genie supports a `net48` build target that runs on Mac and Linux via the [Mono runtime](https://www.mono-project.com/).

## Prerequisites

**macOS:**
```bash
brew install mono
```

**Linux (Debian/Ubuntu):**
```bash
sudo apt install mono-complete
```

## Build

Mono ships its own `msbuild` which includes the .NET Framework 4.8 reference assemblies.
The `dotnet` CLI cannot build `net48` on Mac/Linux — use `msbuild` from the Mono install.

```bash
msbuild Genie4.csproj /p:TargetFramework=net48
```

If `msbuild` is not on your PATH after installing Mono from mono-project.com:
```bash
/Library/Frameworks/Mono.framework/Versions/Current/bin/msbuild Genie4.csproj /p:TargetFramework=net48
```

> **Note:** The Homebrew `mono` formula (6.14) ships `xbuild` which is too old for SDK-style
> projects. Install Mono from [mono-project.com](https://www.mono-project.com/download/stable/)
> to get a working `msbuild`.

## Run

```bash
mono bin/Debug/net48/Genie.exe
```

## Known Limitations

| Feature | Status | Notes |
|---------|--------|-------|
| Core MUD client | ✅ Works | TCP, game parsing, text output |
| UI forms and controls | ✅ Works | Mono WinForms |
| Scripting (JS, ANTLR) | ✅ Works | |
| Auto-mapper | ✅ Works | |
| Configuration/profiles | ✅ Works | |
| `Interaction.Beep()` | ⚠️ May no-op | Maps to `Console.Beep()` on Mono; silently no-ops on some Linux terminals |
| Sound (`.wav` playback) | ❌ No-op | `winmm.dll` not available |
| Text-to-speech | ❌ No-op | `System.Speech.Synthesis` is Windows-only |
| Window flashing | ❌ No-op | |
| Custom window skin drag/resize | ❌ No-op | Standard OS window decorations used instead |
| High-DPI awareness | ❌ No-op | Windows/.NET 5+ only |
| RichTextBox scroll optimization | ⚠️ Reduced | `Win32Utility` redraw suppression absent; may have minor visual flicker |
| RichTextBox character formatting | ⚠️ No-op | `EM_SETCHARFORMAT` skipped |
| Command-line arguments | ⚠️ Ignored | `Interaction.Command()` returns `""` on Mono. Future fix: thread `args` from `Main()` through `DirectConnect()`. |
| Plugin system | ❌ Not supported | The VB.NET plugin host (`Plugins.vbproj`) is excluded from the net48 build. See future work below. |
| Folder/file menu items ("Files >" submenu) | ❌ Crash | Menu items that launch `explorer.exe` or `notepad.exe` (Genie, Maps, Plugins, Scripts, Logs, Art directories) will throw an exception on Mac/Linux. Avoid using these menu items on Mono. |
| `Handle.ToInt32()` on 64-bit | ⚠️ Pre-existing bug | Multiple call sites in `ComponentRichTextBox.cs` may overflow on 64-bit. Deferred — unrelated to this porting work. |

## Plugin System — Future Work

Plugins do not load on Mac or Linux. Future options:

1. Verify Mono VB.NET compiler (`vbnc`) support and re-enable the plugin project for `net48`
2. Migrate plugin interfaces to a cross-platform host (e.g., .NET 6+ with Avalonia)
3. Rewrite plugin interfaces in C# targeting `net48`

## Windows CI Validation

The `net48` target can be validated on Windows without a Mac:

```bash
dotnet build -f net48
```

(Requires the .NET Framework 4.8 targeting pack.)
