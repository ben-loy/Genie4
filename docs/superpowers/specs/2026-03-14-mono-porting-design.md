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
- `Microsoft.VisualBasic` NuGet package reference conditioned on `net6.0-windows` (it is a framework assembly on net48 and does not need a NuGet reference). The `<Import Include="Microsoft.VisualBasic" />` element is a legacy VB project system artifact that is inert in SDK-style C# projects — leave it unchanged.
- `Microsoft.Extensions.Hosting` version 6.0.0 supports `net461+` — leave the reference unconditional.
- `System.Speech` (`System.Speech.Synthesis`) is an implicit Windows platform framework reference on `net6.0-windows` — there is no explicit `<PackageReference>` in the csproj and none needs to be added or conditioned. The `#if WINDOWS` source guards are sufficient.
- `<ProjectReference>` to `Plugin\Plugins.vbproj` conditioned on `net6.0-windows` (plugin system is Windows-only; see Future Work).
- `Utility/EnumWindows.cs` excluded from `net48` build via `<Compile Remove>` condition. Confirmed dead code: not referenced outside its own file, including the `WindowStyleFlags` and `ExtendedWindowStyleFlags` enums defined within it. No stubs needed.
- `Interfaces.dll` in `Libs/` was originally declared with `processorArchitecture=MSIL` — it is a pure .NET assembly and is compatible with net48/Mono. `LegacyPluginHost.cs` and `PluginHost.cs` reference `GeniePlugin.Interfaces.IHost` from this lib and require no changes. Leave the `<HintPath>` reference unconditional.

## Conditional Compilation (`#if WINDOWS`)

### `Program.cs`
- `Application.SetHighDpiMode(HighDpiMode.SystemAware)` does not exist in net48/Mono — wrap in `#if WINDOWS`.
- `Host.CreateDefaultBuilder()` DI host is compatible with net48 (package supports `net461+`) — no change needed.

### `Forms/FormMain.cs` — `using Accessibility`
`using Accessibility;` on line 14 references a COM interop assembly that may not be available on all Mono installations. Confirmed: the `Accessibility.` namespace is not used anywhere in the file. Remove this `using` directive entirely.

### `Utility/Sound.cs`
Wrap in `#if WINDOWS`: both `DllImport` declarations for `winmm.dll` (two overloads of `PlaySound`) AND all four method bodies (`PlayWaveFile`, `PlayWaveResource`, `PlayWaveSystem`, `StopPlaying`). The `using Microsoft.VisualBasic.CompilerServices;` directive can remain — `CompilerServices` is a framework assembly on net48 and the unused import is harmless. Non-Windows stubs are empty no-ops (void return, nothing else).

### `Utility/Win32Utility.cs`

Wrap **all** `DllImport` declarations in `#if WINDOWS`, including the three private P/Invokes at lines 8–13 (`GetTopWindow`, `GetNextWindow`, `SendMessageA`) — these are private with no outside callers so no stubs are needed for them. Also wrap `GetScrollInfo` and `SetScrollInfo` (private, no stubs needed).

**Public method stub guidance:**

- `BeginUpdate` and `EndUpdate` — call the public `SendMessage` stub. Their method bodies compile and silently no-op through the stub chain. **No body guard needed.**
- `GetFirstLineVisible` and `LineScroll` — also call the public `SendMessage` stub. **No body guard needed** for the same reason.
- `GetScrollPos` and `SetScrollPos` — call the private `GetScrollInfo`/`SetScrollInfo` which are guarded out. Their method bodies **must** be wrapped in `#if WINDOWS` with no-op stubs (return `0` / `false`).
- `ReleaseCapture` stub returns `false`. `SendMessage` (public skinning overload) stub returns `IntPtr.Zero`. These are used by `FormSkin.cs` for custom borderless window dragging/resizing — on Mac/Linux the drag/resize is non-functional, but the app runs with standard OS window decorations.

**`ComponentRichTextBox.cs` coupling note:** Calls `Win32Utility.BeginUpdate`, `EndUpdate`, `GetFirstLineVisible`, and `LineScroll` extensively. All silently no-op via the stub chain — no changes to `ComponentRichTextBox.cs` needed for these calls.

### `Utility/WindowFlash.cs`
Wrap the single `DllImport` for `user32.dll` `FlashWindow` in `#if WINDOWS`. The class compiles on all platforms; the P/Invoke is absent on net48.

### `Forms/FormMain.cs` — FlashWindow call site
The actual call `NativeMethods.FlashWindow(Handle, true)` is inside the private `FlashWindow()` method (line ~7799). Wrap this method body in `#if WINDOWS`. The `SafeFlashWindow()` wrapper and `Command_FlashWindow` event handler require no changes — they call the now-empty `FlashWindow()` body harmlessly.

### `Forms/FormMain.cs` — Speech Synthesis
`using System.Speech.Synthesis;` and all `SpeechSynthesizer` usage wrapped in `#if WINDOWS`.

### `Forms/Components/ComponentRichTextBox.cs`
Contains its own `[DllImport("user32.dll")]` for a private `SendMessage` overload used for `EM_SETCHARFORMAT` (line ~52). Wrap the `DllImport` declaration and its single call site in `#if WINDOWS`. On Mac/Linux the character format call is silently skipped; text still renders.

**Pre-existing issue (deferred):** Multiple call sites in this file use `Handle.ToInt32()` when passing an `IntPtr` to Win32 methods. On 64-bit platforms this can overflow. This is a pre-existing bug unrelated to this porting work — noted in Known Limitations.

## Known Limitations on Mac/Linux

| Feature | Status | Notes |
|---------|--------|-------|
| Core MUD client (TCP, parsing, output) | ✅ Works | |
| All UI forms and controls | ✅ Works | Mono WinForms |
| Scripting (Jint JS, ANTLR) | ✅ Works | Pre-compiled MSIL libs, net4x compatible |
| Auto-mapper | ✅ Works | |
| Configuration/profiles | ✅ Works | |
| Plugin host interfaces (`Interfaces.dll`) | ✅ Works | MSIL assembly, net48/Mono compatible |
| DI host (`Microsoft.Extensions.Hosting`) | ✅ Works | Package supports net461+ |
| `Interaction.Beep()` | ⚠️ May no-op | Maps to `Console.Beep()` on Mono; silently no-ops on some Linux terminals. No code changes needed. |
| `Interaction.Command()` | ⚠️ Returns empty string | `FormMain.cs` calls `Interaction.Command()` to parse startup args. On Mono this returns `""`, so command-line arguments are ignored. The app starts normally without them. Future fix: pass `args` from `Main()` through `DirectConnect()` instead. |
| High-DPI awareness | ❌ No-op | `SetHighDpiMode` is Windows/.NET 5+ only; guarded out |
| Sound (`.wav` playback) | ❌ No-op | `winmm.dll` not available on Mac/Linux |
| Text-to-speech | ❌ No-op | `System.Speech.Synthesis` is Windows-only |
| Window flashing | ❌ No-op | `NativeMethods.FlashWindow` call guarded in `FormMain.cs` |
| Custom skin drag/resize | ❌ No-op | `Win32Utility` stubs; standard OS window decorations used instead |
| RichTextBox scroll optimization | ⚠️ No-op | `Win32Utility` redraw suppression absent; may have minor visual flicker |
| RichTextBox character formatting | ⚠️ No-op | `ComponentRichTextBox` `EM_SETCHARFORMAT` call skipped |
| Plugin system | ❌ Not supported | See Future Work below |
| `Handle.ToInt32()` on 64-bit (pre-existing) | ⚠️ Bug | Multiple call sites in `ComponentRichTextBox.cs`. Deferred — unrelated to this porting work. |

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

**Mac/Linux (build the project directly to avoid sln-level TargetFramework override ambiguity):**
```bash
msbuild Genie4.csproj /p:TargetFramework=net48
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
(Requires the .NET Framework 4.8 targeting pack.) This allows Windows CI to validate both targets without a Mac agent.

## Files Changed

| File | Change |
|------|--------|
| `Genie4.csproj` | Multi-target, conditional properties/references, `WINDOWS` define, exclude `EnumWindows.cs` for net48 |
| `Program.cs` | `#if WINDOWS` around `Application.SetHighDpiMode` |
| `Forms/FormMain.cs` | Remove unused `using Accessibility;`; `#if WINDOWS` around `NativeMethods.FlashWindow` call and `System.Speech.Synthesis` usage |
| `Utility/Sound.cs` | `#if WINDOWS` around `winmm.dll` P/Invoke declarations + all four method bodies |
| `Utility/WindowFlash.cs` | `#if WINDOWS` around `user32.dll` P/Invoke |
| `Utility/Win32Utility.cs` | `#if WINDOWS` around all P/Invoke declarations; `#if WINDOWS` body guards + no-op stubs for `GetScrollPos` and `SetScrollPos`; no-op stubs for `ReleaseCapture` and skinning `SendMessage` |
| `Forms/Components/ComponentRichTextBox.cs` | `#if WINDOWS` around `user32.dll` P/Invoke declaration + `EM_SETCHARFORMAT` call site |
| `docs/MONO-PORTING.md` | New — user-facing limitations and build instructions |
| `docs/superpowers/specs/2026-03-14-mono-porting-design.md` | This document |
