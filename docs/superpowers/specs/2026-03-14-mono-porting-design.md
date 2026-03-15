# Mono Porting Design — Genie4 Mac/Linux Build

**Date:** 2026-03-14
**Status:** Approved

## Goal

Add a `net48` build target to the existing `Genie4.csproj` so the application can be compiled and run on Mac and Linux using the Mono runtime, alongside the unchanged Windows `net6.0-windows` build.

## Approach

Multi-target the existing `Genie4.csproj` using `<TargetFrameworks>net6.0-windows;net48</TargetFrameworks>`. Use a `WINDOWS` preprocessor constant (defined only for the `net6.0-windows` target) to guard Windows-specific P/Invoke and API calls. No separate project file is introduced; both targets build from the same source.

## Project File Changes (`Genie4.csproj`)

- `<TargetFramework>` → `<TargetFrameworks>net6.0-windows;net48</TargetFrameworks>`
- `<UseWindowsForms>` and `<ImportWindowsDesktopTargets>` conditioned on `net6.0-windows` only
- Add `<DefineConstants>WINDOWS</DefineConstants>` conditioned on `net6.0-windows`
- `Microsoft.VisualBasic` NuGet package reference conditioned on `net6.0-windows` (built-in on net48)
- `<ProjectReference>` to `Plugin\Plugins.vbproj` conditioned on `net6.0-windows` (plugin system is Windows-only)
- `Utility/EnumWindows.cs` excluded from `net48` build via `<Compile Remove>` condition (dead code — not called anywhere, no stub needed)

## Conditional Compilation (`#if WINDOWS`)

### `Utility/Sound.cs`
All `DllImport` declarations for `winmm.dll` and all method implementations (`PlayWaveFile`, `PlayWaveResource`, `PlayWaveSystem`, `StopPlaying`) wrapped in `#if WINDOWS`. Non-Windows stubs are empty no-ops.

### `Utility/WindowFlash.cs`
`DllImport` for `user32.dll` `FlashWindow` wrapped in `#if WINDOWS`. Callers silently skip on Mac/Linux.

### `Utility/Win32Utility.cs`
All `DllImport` declarations for `user32.dll` wrapped in `#if WINDOWS`. Public methods (`BeginUpdate`, `EndUpdate`, `GetFirstLineVisible`, `LineScroll`, `GetScrollPos`, `SetScrollPos`) get no-op stubs on non-Windows. RichTextBox still functions; the Windows-specific redraw-suppression optimization is simply absent.

### `Forms/FormMain.cs`
`using System.Speech.Synthesis;` and all `SpeechSynthesizer` usage wrapped in `#if WINDOWS`.

## Known Limitations on Mac/Linux

| Feature | Status | Notes |
|---------|--------|-------|
| Core MUD client (TCP, parsing, output) | ✅ Works | |
| All UI forms and controls | ✅ Works | Mono WinForms |
| Scripting (Jint JS, ANTLR) | ✅ Works | Pre-compiled libs are net4x compatible |
| Auto-mapper | ✅ Works | |
| Configuration/profiles | ✅ Works | |
| Sound (`.wav` playback) | ❌ No-op | `winmm.dll` not available on Mac/Linux |
| Text-to-speech | ❌ No-op | `System.Speech.Synthesis` is Windows-only |
| Window flashing | ❌ No-op | `user32.dll` `FlashWindow` not available |
| RichTextBox scroll optimization | ⚠️ No-op | May have minor visual flicker on large output |
| Plugin system | ❌ Not supported | See future work below |

## Plugin System — Future Work

The VB.NET plugin host (`Plugin/Plugins.vbproj`) is excluded from the `net48` build. Plugins will not load on Mac or Linux.

**Future options to address this:**
1. Verify Mono VB.NET compiler (`vbnc`) support and enable the plugin project for `net48`
2. Migrate the plugin API (`IHost`/`IPlugin`) to a cross-platform host using .NET 6+ with Avalonia or another cross-platform UI framework
3. Rewrite plugin interfaces in C# targeting `net48` to remove the VB.NET dependency

## Build Instructions

### Prerequisites (Mac/Linux)
```bash
brew install mono          # macOS via Homebrew
# or: sudo apt install mono-complete  (Debian/Ubuntu)
```

### Build

**Windows (unchanged):**
```bash
dotnet build -f net6.0-windows
```

**Mac/Linux:**
```bash
msbuild Genie4.sln /p:TargetFramework=net48
```

**Run on Mac/Linux:**
```bash
mono bin/net48/Genie.exe
```

### CI Validation

The `net48` target can also be built on Windows via:
```bash
dotnet build -f net48
```
(Requires the .NET Framework 4.8 targeting pack.) This allows Windows CI to validate both targets without requiring a Mac agent.

## Files Changed

| File | Change |
|------|--------|
| `Genie4.csproj` | Multi-target, conditional properties/references, `WINDOWS` define |
| `Utility/Sound.cs` | `#if WINDOWS` around P/Invoke + all implementations |
| `Utility/WindowFlash.cs` | `#if WINDOWS` around P/Invoke |
| `Utility/Win32Utility.cs` | `#if WINDOWS` around P/Invoke, no-op stubs for public methods |
| `Forms/FormMain.cs` | `#if WINDOWS` around `System.Speech.Synthesis` usage |
| `docs/MONO-PORTING.md` | New — user-facing limitations and build instructions |
| `docs/superpowers/specs/2026-03-14-mono-porting-design.md` | This document |
