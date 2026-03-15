# Mono Porting Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `net48` build target to `Genie4.csproj` so the app compiles and runs on Mac/Linux via Mono alongside the unchanged Windows `net6.0-windows` build.

**Architecture:** Multi-target `<TargetFrameworks>net6.0-windows;net48</TargetFrameworks>` in the existing csproj. A `WINDOWS` preprocessor constant (defined only for `net6.0-windows`) guards all Windows-specific P/Invoke and APIs. No new project files.

**Tech Stack:** C# / .NET 6 (Windows) + .NET Framework 4.8 (Mono), Windows Forms, MSBuild multi-targeting.

**Spec:** `docs/superpowers/specs/2026-03-14-mono-porting-design.md`

---

## Chunk 1: Project File + Entry Point

### Task 1: Multi-target `Genie4.csproj`

**Files:**
- Modify: `Genie4.csproj`

The csproj currently has a single `<TargetFramework>` and several Windows-only properties that must be conditioned. Make all changes in one edit so the file stays consistent.

- [ ] **Step 1: Update `Genie4.csproj`**

Replace line 3:
```xml
<TargetFramework>net6.0-windows</TargetFramework>
```
With:
```xml
<TargetFrameworks>net6.0-windows;net48</TargetFrameworks>
```

Condition the Windows-only properties on lines 38–39 (currently inside the top `<PropertyGroup>`):
```xml
<UseWindowsForms Condition="'$(TargetFramework)' == 'net6.0-windows'">true</UseWindowsForms>
<ImportWindowsDesktopTargets Condition="'$(TargetFramework)' == 'net6.0-windows'">true</ImportWindowsDesktopTargets>
```

Add a `WINDOWS` define to the top `<PropertyGroup>` (after the existing `<LangVersion>` line is fine). Use the additive form to avoid clobbering other constants:
```xml
<DefineConstants Condition="'$(TargetFramework)' == 'net6.0-windows'">$(DefineConstants);WINDOWS</DefineConstants>
```

**Important:** The Debug `<PropertyGroup>` (lines 42–51) contains an empty `<DefineConstants></DefineConstants>` at lines 48–49. MSBuild evaluates `PropertyGroup` blocks in order and the last definition wins, so this empty element would silently erase `WINDOWS` in every Debug build. Delete lines 48–49 entirely from the Debug `PropertyGroup`:
```xml
<!-- DELETE these two lines from the Debug PropertyGroup: -->
    <DefineConstants>
    </DefineConstants>
```
The Release `PropertyGroup` has no `<DefineConstants>` element and requires no change.

Condition the `Microsoft.VisualBasic` NuGet package reference (line 217) — it is a framework assembly on net48 and must not be pulled from NuGet there:
```xml
<PackageReference Include="Microsoft.VisualBasic" Version="10.3.0"
  Condition="'$(TargetFramework)' == 'net6.0-windows'" />
```

Condition the plugin project reference (line 204) — plugin system is Windows-only:
```xml
<ProjectReference Include="Plugin\Plugins.vbproj"
  Condition="'$(TargetFramework)' == 'net6.0-windows'" />
```

Add an exclusion for `EnumWindows.cs` from the net48 build (dead code, safe to exclude). Add this inside a new `<ItemGroup>`:
```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <Compile Remove="Utility\EnumWindows.cs" />
</ItemGroup>
```

Leave `Microsoft.Extensions.Hosting` unconditional — it supports `net461+`.
Leave `<Import Include="Microsoft.VisualBasic" />` unchanged — it is inert in SDK-style C# projects.
Leave all `<HintPath>` references to `Libs/*.dll` unconditional — they are MSIL assemblies compatible with net48.
Leave `<OutputType>WinExe</OutputType>` unchanged — valid for net48 on Windows and ignored by Mono on Mac/Linux.
Leave `<MyType>WindowsForms</MyType>` unchanged — a VB project system artifact, inert in SDK-style C# projects.
Leave ClickOnce properties (`ManifestCertificateThumbprint`, `PublishUrl`, etc.) unchanged — MSBuild ignores unrecognised properties for a given SDK; they have no effect on the net48 build.

- [ ] **Step 2: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Genie4.csproj
git commit -m "feat: multi-target net6.0-windows and net48 for Mono support"
```

---

### Task 2: Guard `SetHighDpiMode` in `Program.cs`

**Files:**
- Modify: `Program.cs`

`Application.SetHighDpiMode` is a .NET 5+ API absent from net48/Mono.

- [ ] **Step 1: Wrap `SetHighDpiMode` call**

In `Program.cs`, line 14, wrap the call:
```csharp
#if WINDOWS
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
```

- [ ] **Step 2: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat: guard SetHighDpiMode for Mono net48 compatibility"
```

---

## Chunk 2: FormMain.cs Guards

### Task 3: Remove unused Windows `using` directives from `FormMain.cs`

**Files:**
- Modify: `Forms/FormMain.cs`

Two `using` directives reference Windows-only assemblies but are never actually used in the file. Removing them is cleaner than guarding.

Confirmed: a codebase-wide search for `SpeechSynthesizer` finds only this `using` line — the type is never instantiated or called anywhere. Removing the `using` is the complete fix; no `#if WINDOWS` body guard is needed.

- [ ] **Step 1: Remove `using System.Speech.Synthesis;` (line 9)**

Delete the line:
```csharp
using System.Speech.Synthesis;
```

- [ ] **Step 2: Remove `using Accessibility;` (line 14)**

Delete the line:
```csharp
using Accessibility;
```

- [ ] **Step 3: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Forms/FormMain.cs
git commit -m "feat: remove unused Windows-only using directives from FormMain"
```

---

### Task 4: Guard `FlashWindow` call site in `FormMain.cs`

**Files:**
- Modify: `Forms/FormMain.cs`

The `private void FlashWindow()` method (line 7797) calls `NativeMethods.FlashWindow(Handle, true)` which is a Windows P/Invoke. On Mac/Linux the method body should be empty. `SafeFlashWindow()` and `Command_FlashWindow` require no changes.

- [ ] **Step 1: Wrap `FlashWindow()` method body**

Change lines 7797–7800 from:
```csharp
        private void FlashWindow()
        {
            NativeMethods.FlashWindow(Handle, true);
        }
```
To:
```csharp
        private void FlashWindow()
        {
#if WINDOWS
            NativeMethods.FlashWindow(Handle, true);
#endif
        }
```

- [ ] **Step 2: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Forms/FormMain.cs
git commit -m "feat: guard FlashWindow P/Invoke call for Mono net48 compatibility"
```

---

## Chunk 3: Utility Layer Guards

### Task 5: Guard `Utility/WindowFlash.cs`

**Files:**
- Modify: `Utility/WindowFlash.cs`

Contains one `DllImport` for `user32.dll FlashWindow`. The `NativeMethods` class and the P/Invoke declaration inside it need to be guarded.

- [ ] **Step 1: Wrap the `DllImport` in `#if WINDOWS`**

Change:
```csharp
internal static class NativeMethods
{
    static NativeMethods()
    {
    }

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
}
```
To:
```csharp
internal static class NativeMethods
{
    static NativeMethods()
    {
    }

#if WINDOWS
    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
#else
    public static bool FlashWindow(IntPtr hwnd, bool bInvert) => false;
#endif
}
```

The `#else` stub is a defensive measure: it ensures the `NativeMethods` class is still usable on net48 if any future code calls `FlashWindow` directly. Note that Task 4 already guards the only current call site with `#if WINDOWS`, so the stub is not strictly required for this feature — it is included for forward safety.

- [ ] **Step 2: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Utility/WindowFlash.cs
git commit -m "feat: guard WindowFlash DllImport for Mono net48 compatibility"
```

---

### Task 6: Guard `Utility/Sound.cs`

**Files:**
- Modify: `Utility/Sound.cs`

Two `winmm.dll` P/Invoke declarations (lines 8–11, two overloads of `PlaySound`) and all four method bodies must be guarded. Method stubs return void (no-ops).

- [ ] **Step 1: Wrap P/Invoke declarations and method bodies**

Replace the entire file contents (preserving namespace/class structure) so that:

```csharp
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.CompilerServices;

namespace GenieClient.Genie
{
    public class Sound
    {
#if WINDOWS
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        static extern int PlaySound(string name, int hmod, int flags);
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        static extern int PlaySound(byte[] name, int hmod, int flags);
#endif

        public const int SND_SYNC = 0x0;
        public const int SND_ASYNC = 0x1;
        public const int SND_MEMORY = 0x4;
        public const int SND_ALIAS = 0x10000;
        public const int SND_NODEFAULT = 0x2;
        public const int SND_FILENAME = 0x20000;
        public const int SND_RESOURCE = 0x40004;
        public const int SND_PURGE = 0x40;

        public static void PlayWaveFile(string fileWaveFullPath)
        {
#if WINDOWS
            try
            {
                if (fileWaveFullPath.Contains(@"\") == false)
                {
                    fileWaveFullPath = LocalDirectory.Path + @"\Sounds\" + fileWaveFullPath;
                }

                if (Conversions.ToString(fileWaveFullPath[fileWaveFullPath.Length - 4]) != ".")
                {
                    fileWaveFullPath += ".wav";
                }

                Sound.PlaySound(fileWaveFullPath, 0, SND_FILENAME | SND_ASYNC);
            }
            catch
            {
            }
#endif
        }

        public static void PlayWaveResource(string WaveResourceName)
        {
#if WINDOWS
            string strNameSpace = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name.ToString();

            var resourceStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(strNameSpace + "." + WaveResourceName);
            if (resourceStream is null)
                return;

            byte[] wavData;
            wavData = new byte[Conversions.ToInteger(resourceStream.Length) + 1];
            resourceStream.Read(wavData, 0, Conversions.ToInteger(resourceStream.Length));

            PlaySound(wavData, 0, SND_ASYNC | SND_MEMORY);
#endif
        }

        public static void PlayWaveSystem(string SystemWaveName)
        {
#if WINDOWS
            Sound.PlaySound(SystemWaveName, 0, SND_ALIAS | SND_ASYNC | SND_NODEFAULT);
#endif
        }

        public static void StopPlaying()
        {
#if WINDOWS
            string argname = "";
            Sound.PlaySound(argname, 0, SND_NODEFAULT | SND_MEMORY);
#endif
        }
    }
}
```

Note: `using Microsoft.VisualBasic.CompilerServices;` is kept — `Conversions` is a framework assembly on net48 and the unused import on non-Windows builds is harmless.

- [ ] **Step 2: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Utility/Sound.cs
git commit -m "feat: guard winmm.dll P/Invoke in Sound.cs for Mono net48 compatibility"
```

---

### Task 7: Guard `Utility/Win32Utility.cs`

**Files:**
- Modify: `Utility/Win32Utility.cs`

This file has multiple `DllImport` declarations and public methods. The strategy:
- All private P/Invokes → guard with `#if WINDOWS` (no stubs needed, private)
- `GetScrollInfo`/`SetScrollInfo` → guard with `#if WINDOWS` (no stubs needed, private)
- `BeginUpdate`/`EndUpdate` → no body change needed; they call the `SendMessage` stub
- `GetFirstLineVisible`/`LineScroll` → no body change needed; they call the `SendMessage` stub
- `GetScrollPos`/`SetScrollPos` → body must be guarded (they call private `GetScrollInfo`/`SetScrollInfo`)
- `ReleaseCapture` → needs `#else` stub returning `false`
- Public `SendMessage` (skinning overload) → needs `#else` stub returning `IntPtr.Zero`

The `SCROLLINFO` struct (lines 24–33) and the instance field `private SCROLLINFO sc` are plain C# with no Windows dependencies — they compile and are safe to leave unguarded even though the P/Invokes that use them are guarded out.

- [ ] **Step 1: Wrap the three private P/Invokes at the top (lines 8–13)**

```csharp
#if WINDOWS
        [DllImport("user32")]
        private static extern int GetTopWindow(int hwnd);
        [DllImport("user32", EntryPoint = "GetWindow")]
        private static extern int GetNextWindow(int hwnd, int wFlag);
        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        private static extern int SendMessageA(IntPtr hwnd, int wMsg, int wParam, int lParam);
#endif
```

- [ ] **Step 2: Wrap the two private scroll P/Invokes (`GetScrollInfo`/`SetScrollInfo`)**

```csharp
#if WINDOWS
        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hWnd, int nBar, SCROLLINFO lpScrollInfo);

        [DllImport("user32.dll")]
        private static extern bool SetScrollInfo(IntPtr hWnd, int nBar, SCROLLINFO lpScrollInfo, bool fRedraw);
#endif
```

- [ ] **Step 3: Wrap `GetScrollPos` and `SetScrollPos` method bodies**

```csharp
        public int GetScrollPos(IntPtr hwnd)
        {
#if WINDOWS
            sc.fMask = SIF_ALL;
            sc.cbSize = Marshal.SizeOf(sc);
            GetScrollInfo(hwnd, SBS_VERT, sc);
            return sc.nPos;
#else
            return 0;
#endif
        }

        public bool SetScrollPos(IntPtr hwnd, int pos)
        {
#if WINDOWS
            sc.fMask = SIF_ALL;
            sc.nPos = pos;
            sc.cbSize = Marshal.SizeOf(sc);
            return SetScrollInfo(hwnd, SBS_VERT, sc, true);
#else
            return false;
#endif
        }
```

- [ ] **Step 4: Wrap the two skinning P/Invokes (`ReleaseCapture` / public `SendMessage`) with stubs**

```csharp
#if WINDOWS
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
#else
        public static bool ReleaseCapture() => false;
        public static IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam) => IntPtr.Zero;
#endif
```

- [ ] **Step 5: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Utility/Win32Utility.cs
git commit -m "feat: guard Win32Utility P/Invoke and add no-op stubs for Mono net48"
```

---

## Chunk 4: ComponentRichTextBox + Docs + Verification

### Task 8: Guard `Forms/Components/ComponentRichTextBox.cs`

**Files:**
- Modify: `Forms/Components/ComponentRichTextBox.cs`

Contains a private `SendMessage` P/Invoke for `EM_SETCHARFORMAT` (line 52) and one call site (line 1007).

- [ ] **Step 1: Wrap the `DllImport` declaration (lines 52–53)**

```csharp
#if WINDOWS
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
#endif
```

- [ ] **Step 2: Wrap the call site (line ~1007)**

Find the line:
```csharp
            var res = SendMessage(handle, EM_SETCHARFORMAT, wpar, lpar);
```
Wrap it:
```csharp
#if WINDOWS
            var res = SendMessage(handle, EM_SETCHARFORMAT, wpar, lpar);
#endif
```

Note: `res` is only used inside the `#if WINDOWS` block — if it is used after this line in the same scope, move the variable declaration inside the guard too. Inspect the surrounding lines (~1000–1015) before editing to confirm.

- [ ] **Step 3: Verify Windows build still compiles**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Forms/Components/ComponentRichTextBox.cs
git commit -m "feat: guard EM_SETCHARFORMAT DllImport in ComponentRichTextBox for Mono net48"
```

---

### Task 9: Attempt `net48` build and fix any remaining errors

This task is a diagnostic sweep. Run the net48 build; fix any compilation errors not covered by the earlier tasks before proceeding to docs.

- [ ] **Step 1: Run net48 build**

```bash
dotnet build -f net48 2>&1 | head -60
```

- [ ] **Step 2: For each compiler error, apply a `#if WINDOWS` guard at the offending line**

Common patterns to look for:
- `error CS0246: type or namespace 'X' could not be found` → missing Windows-only type, add guard
- `error CS0103: name 'X' does not exist` → Windows-only method or constant, add guard
- Repeat build after each fix.

- [ ] **Step 3: Once build succeeds, commit any additional fixes**

Stage only the files you modified (do not use `git add -A`):
```bash
git add <file1> <file2> ...
git commit -m "feat: fix remaining net48 compilation errors"
```

---

### Task 10: Write `docs/MONO-PORTING.md`

**Files:**
- Create: `docs/MONO-PORTING.md`

User-facing documentation covering prerequisites, build steps, and known limitations.

- [ ] **Step 1: Create `docs/MONO-PORTING.md`**

```markdown
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

```bash
msbuild Genie4.csproj /p:TargetFramework=net48
```

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
| RichTextBox scroll optimization | ⚠️ No-op | `Win32Utility` redraw suppression absent; may have minor visual flicker |
| RichTextBox character formatting | ⚠️ No-op | `EM_SETCHARFORMAT` skipped |
| Command-line arguments | ⚠️ Ignored | `Interaction.Command()` returns `""` on Mono. Future fix: thread `args` from `Main()` through `DirectConnect()`. |
| Plugin system | ❌ Not supported | The VB.NET plugin host (`Plugins.vbproj`) is excluded from the net48 build. See future work below. |
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
```

- [ ] **Step 2: Commit**

```bash
git add docs/MONO-PORTING.md
git commit -m "docs: add MONO-PORTING.md with Mac/Linux build instructions and known limitations"
```

---

### Task 11: Final verification

- [ ] **Step 1: Confirm Windows build is clean**

```bash
dotnet build -f net6.0-windows
```
Expected: Build succeeded, 0 errors, 0 warnings related to this work.

- [ ] **Step 2: Confirm net48 build is clean**

```bash
dotnet build -f net48
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: If on Mac, do a smoke-test run**

```bash
msbuild Genie4.csproj /p:TargetFramework=net48
mono bin/Debug/net48/Genie.exe
```
Expected: App launches, main window appears, connect dialog works.

- [ ] **Step 4: Final commit if any loose changes remain**

```bash
git status
# If clean, nothing to do. If dirty, stage only modified files:
git add <modified-file1> <modified-file2> ...
git commit -m "chore: final cleanup for Mono net48 porting"
```
