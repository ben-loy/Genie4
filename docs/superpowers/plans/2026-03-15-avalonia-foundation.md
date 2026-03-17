# Avalonia Foundation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Desktop/Genie4.Desktop.csproj` Avalonia 11 project that builds and runs on Mac/Linux, connecting to the DragonRealms game server and displaying text output, with no changes to the existing Windows build.

**Architecture:** Standalone `Desktop/` project alongside `Genie4.csproj`. Shared Core/Lists/Script/Utility source is included via `<Compile Include="../...">` relative paths. The Desktop project targets `net8.0` and uses Avalonia 11 for UI. A placeholder MainWindow provides: connect bar (account, password, character, connect button), a scrolling text output area, and a command input field wired to `Game.SendText`.

**Tech Stack:** C# / .NET 8, Avalonia 11.3.x, `Microsoft.Extensions.Hosting` for DI, `System.Drawing.Common` for cross-platform `System.Drawing.Color`, `Microsoft.VisualBasic` NuGet for shared code compatibility.

**Spec:** `docs/superpowers/specs/2026-03-14-avalonia-foundation-design.md`

---

## Chunk 1: Source Pre-work — Remove Unused WinForms Imports + Guard Updater

Three shared files have `using System.Windows.Forms;` that won't compile in a `net8.0` Avalonia project. Two are fully unused; one (`Updater.cs`) uses `MessageBox.Show` at one call site.

### Task 1: Remove unused `using System.Windows.Forms;` from `Core/Command.cs` and `Script/Script.cs`

**Files:**
- Modify: `Core/Command.cs` (line 10)
- Modify: `Script/Script.cs` (line 9)

No WinForms types are actually referenced in either file body — the imports are dead code left over from earlier development.

- [ ] **Step 1: Delete line 10 from `Core/Command.cs`**

Remove:
```csharp
using System.Windows.Forms;
```

- [ ] **Step 2: Delete line 9 from `Script/Script.cs`**

Remove:
```csharp
using System.Windows.Forms;
```

- [ ] **Step 3: Verify Windows build is still clean**

```bash
dotnet build Genie4.csproj -f net6.0-windows 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Core/Command.cs Script/Script.cs
git commit -m "chore: remove unused System.Windows.Forms imports from Command and Script"
```

---

### Task 2: Guard `MessageBox.Show` in `Utility/Updater.cs`

**Files:**
- Modify: `Utility/Updater.cs`

`Updater.cs` uses `MessageBox.Show` at line 94 and `MessageBoxButtons`/`DialogResult` from WinForms. The `UpdateUpdater` method is the only place. Guard with `#if !DESKTOP` (not `#if WINDOWS`) so that:
- `net6.0-windows` and `net48` builds retain the interactive dialog (original behaviour preserved)
- `net8.0` Desktop build (where `DESKTOP` will be defined in Task 3) silently skips the update dialog

The `DESKTOP` constant is defined in `Desktop/Genie4.Desktop.csproj` (Task 3). This guard must be added before the Desktop project builds, but `DESKTOP` is only active in the Desktop project context — the existing `Genie4.csproj` builds are unaffected.

- [ ] **Step 1: Guard the `using` and the `MessageBox.Show` call site**

In `Utility/Updater.cs`, change the WinForms `using` at line 11:

```csharp
#if !DESKTOP
using System.Windows.Forms;
#endif
```

Then in the `UpdateUpdater` method (around line 92–94), change:
```csharp
if (!UpdaterIsCurrent && !autoUpdate && MessageBox.Show(@"An updated version of Lamp is available. It is recommended to update Lamp before continuing. Would you like to update now?", "Update Lamp?", MessageBoxButtons.YesNoCancel) != DialogResult.Yes) return;
```
To:
```csharp
#if !DESKTOP
            if (!UpdaterIsCurrent && !autoUpdate && MessageBox.Show(@"An updated version of Lamp is available. It is recommended to update Lamp before continuing. Would you like to update now?", "Update Lamp?", MessageBoxButtons.YesNoCancel) != DialogResult.Yes) return;
#else
            if (!UpdaterIsCurrent && !autoUpdate) return; // Desktop: no dialog, skip silently
#endif
```

- [ ] **Step 2: Verify Windows build is still clean**

```bash
dotnet build Genie4.csproj -f net6.0-windows 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Utility/Updater.cs
git commit -m "chore: guard WinForms MessageBox in Updater.cs with #if !DESKTOP"
```

---

## Chunk 2: Desktop Project Scaffold

### Task 3: Create `Desktop/Genie4.Desktop.csproj`

**Files:**
- Create: `Desktop/Genie4.Desktop.csproj`

The project:
- Targets `net8.0` (cross-platform, no `-windows` suffix)
- References Avalonia 11.3.12 packages
- Includes shared Core, Lists, Script, Utility source files via relative `<Compile Include>` paths
- Excludes `Macros.cs` and `KeyCode.cs` — both depend on `System.Windows.Forms.Keys` as a data type; macro/keybinding support is deferred to a later sub-project
- References the three Libs DLLs via relative `HintPath`

- [ ] **Step 1: Create `Desktop/` directory and `Genie4.Desktop.csproj`**

Create `Desktop/Genie4.Desktop.csproj` with the following content:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>warnings</Nullable>
    <LangVersion>latest</LangVersion>
    <RootNamespace>GenieClient.Desktop</RootNamespace>
    <AssemblyName>Genie.Desktop</AssemblyName>
    <!-- DESKTOP symbol used by shared files to conditionalize WinForms-dependent code -->
    <DefineConstants>$(DefineConstants);DESKTOP</DefineConstants>
  </PropertyGroup>

  <!-- Avalonia -->
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
  </ItemGroup>

  <!-- Shared dependencies -->
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.0" />
    <PackageReference Include="Microsoft.VisualBasic" Version="10.3.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="6.0.0" />
    <PackageReference Include="System.Resources.Extensions" Version="8.0.0" />
  </ItemGroup>

  <!-- Libs DLLs (MSIL, compatible with net8.0) -->
  <ItemGroup>
    <Reference Include="Antlr3.Runtime">
      <HintPath>../Libs/Antlr3.Runtime.dll</HintPath>
    </Reference>
    <Reference Include="Interfaces">
      <HintPath>../Libs/Interfaces.dll</HintPath>
    </Reference>
    <Reference Include="Jint">
      <HintPath>../Libs/Jint.dll</HintPath>
    </Reference>
  </ItemGroup>

  <!-- Shared Core -->
  <ItemGroup>
    <Compile Include="../Core/Command.cs" />
    <Compile Include="../Core/Connection.cs" />
    <Compile Include="../Core/Game.cs" />
    <Compile Include="../Core/GenieError.cs" />
  </ItemGroup>

  <!-- Shared Lists.
       Spec lists: ArrayList, Classes, CommandQueue, Config, Globals, Highlights, Macros, Names, SortedList.
       Additional files included because Globals.cs depends on them: Aliases, CollectionList, Events, ScriptList, VolatileHighlight.
       Macros.cs and KeyCode.cs included; WinForms.Keys usage inside them is guarded #if !DESKTOP in Task 4. -->
  <ItemGroup>
    <Compile Include="../Lists/Aliases.cs" />
    <Compile Include="../Lists/ArrayList.cs" />
    <Compile Include="../Lists/Classes.cs" />
    <Compile Include="../Lists/CollectionList.cs" />
    <Compile Include="../Lists/CommandQueue.cs" />
    <Compile Include="../Lists/Config.cs" />
    <Compile Include="../Lists/Events.cs" />
    <Compile Include="../Lists/Globals.cs" />
    <Compile Include="../Lists/Highlights.cs" />
    <Compile Include="../Lists/Macros.cs" />
    <Compile Include="../Lists/Names.cs" />
    <Compile Include="../Lists/ScriptList.cs" />
    <Compile Include="../Lists/SortedList.cs" />
    <Compile Include="../Lists/VolatileHighlight.cs" />
  </ItemGroup>

  <!-- Shared Script -->
  <ItemGroup>
    <Compile Include="../Script/Eval.cs" />
    <Compile Include="../Script/JavaScript.cs" />
    <Compile Include="../Script/LUAScript.cs" />
    <Compile Include="../Script/MathEval.cs" />
    <Compile Include="../Script/Script.cs" />
    <Compile Include="../Script/Trace.cs" />
  </ItemGroup>

  <!-- Shared Utility (EnumWindows.cs excluded — Windows-only P/Invoke without guard).
       Script/Trace.cs added to Script group; present on disk, needed for Script.cs compilation.
       KeyCode.cs included; StringToKey() method that returns System.Windows.Forms.Keys is guarded #if !DESKTOP in Task 4. -->
  <ItemGroup>
    <Compile Include="../Utility/ColorCode.cs" />
    <Compile Include="../Utility/Crypto.cs" />
    <Compile Include="../Utility/DownloadResult.cs" />
    <Compile Include="../Utility/EmbeddedAssembly.cs" />
    <Compile Include="../Utility/FileHandler.cs" />
    <Compile Include="../Utility/INaturalComparer.cs" />
    <Compile Include="../Utility/KeyCode.cs" />
    <Compile Include="../Utility/Log.cs" />
    <Compile Include="../Utility/PluginServices.cs" />
    <Compile Include="../Utility/Sound.cs" />
    <Compile Include="../Utility/Updater.cs" />
    <Compile Include="../Utility/Utility.cs" />
    <Compile Include="../Utility/Win32Utility.cs" />
    <Compile Include="../Utility/WindowFlash.cs" />
    <Compile Include="../Utility/XMLConfig.cs" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Attempt first build to baseline errors**

```bash
dotnet build Desktop/Genie4.Desktop.csproj 2>&1 | grep -E "^.*error" | head -20
```

Expected: Several errors about missing types (`Macros`, `KeyCode`) — these are resolved in subsequent tasks. If errors are different from expected, note them for Task 8.

- [ ] **Step 3: Commit skeleton csproj**

```bash
git add Desktop/Genie4.Desktop.csproj
git commit -m "feat: add Desktop/Genie4.Desktop.csproj Avalonia net8.0 skeleton"
```

---

### Task 4: Guard WinForms.Keys usage in `Macros.cs`, `KeyCode.cs`, and `Globals.cs`

**Files:**
- Modify: `Lists/Macros.cs`
- Modify: `Utility/KeyCode.cs`
- Modify: `Lists/Globals.cs`

Both `Macros.cs` and `KeyCode.cs` are included in the Desktop project (per spec). Their `System.Windows.Forms.Keys` usage must be guarded so they compile on `net8.0`.

**Strategy per file:**
- `Macros.cs`: uses `System.Windows.Forms.Keys` as a local variable type in macro lookup methods. Guard the WinForms import and any `Keys`-typed code with `#if !DESKTOP`.
- `KeyCode.cs`: `StringToKey()` returns `System.Windows.Forms.Keys`. Guard the method and its import with `#if !DESKTOP`; add a `#else` stub returning `0` (default int cast to the enum).
- `Globals.cs`: `MacroList` field is of type `Macros` — this compiles fine since `Macros.cs` is included. No change needed to `Globals.cs` itself unless `MacroList` uses any guarded member.

**`#if !DESKTOP` is used** (not `#if WINDOWS`) so that `net48` builds (which also lack `DESKTOP`) preserve their existing WinForms behaviour.

- [ ] **Step 1: Guard `using System.Windows.Forms;` and `Keys`-typed code in `Lists/Macros.cs`**

Find all `Keys` references:
```bash
grep -n "Keys\b\|Windows\.Forms" Lists/Macros.cs
```

Guard the `using` at the top and each `Keys`-typed local variable or comparison:
```csharp
// At top of file:
#if !DESKTOP
using System.Windows.Forms;
#endif

// Around each local variable of type Keys and each comparison like:
//   oKey = KeyCode.StringToKey(sKey);
//   if (oKey == System.Windows.Forms.Keys.None)
// wrap with:
#if !DESKTOP
    ... keys-dependent block ...
#endif
```

Read the file before editing to identify exact line numbers and surrounding context.

- [ ] **Step 2: Guard `StringToKey` in `Utility/KeyCode.cs`**

`StringToKey()` returns `System.Windows.Forms.Keys`. Guard the method and the `using`:

```csharp
// At top:
#if !DESKTOP
using System.Windows.Forms;
#endif

// Replace the StringToKey method with:
#if !DESKTOP
        public static System.Windows.Forms.Keys StringToKey(string sHotkey)
        {
            try
            {
                return (System.Windows.Forms.Keys)Conversions.ToInteger(new KeysConverter().ConvertFromString(sHotkey));
            }
            #pragma warning disable CS0168
            catch (Exception ex)
            #pragma warning restore CS0168
            {
                return default;
            }
        }
#else
        public static int StringToKey(string sHotkey) => 0; // Stub: macros not supported on Desktop
#endif
```

Note: `KeyCode.Keys` (the custom integer enum defined in the same file) compiles fine without WinForms — only `StringToKey` needs guarding.

- [ ] **Step 3: Build and check**

```bash
dotnet build Desktop/Genie4.Desktop.csproj 2>&1 | grep -E "error" | head -20
```

Fix any remaining errors. If `Command.cs` or other shared files reference `MacroList` members that now fail, guard those call sites with `#if !DESKTOP`.

- [ ] **Step 4: Verify Windows build still clean**

```bash
dotnet build Genie4.csproj -f net6.0-windows 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add Lists/Macros.cs Utility/KeyCode.cs
git commit -m "feat: guard WinForms.Keys usage in Macros.cs and KeyCode.cs with #if !DESKTOP"
```

---

### Task 5: Create Avalonia app entry point and application class

**Files:**
- Create: `Desktop/Program.cs`
- Create: `Desktop/App.axaml`
- Create: `Desktop/App.axaml.cs`

- [ ] **Step 1: Create `Desktop/Program.cs`**

```csharp
using Avalonia;

namespace GenieClient.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
```

- [ ] **Step 2: Create `Desktop/App.axaml`**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="GenieClient.Desktop.App">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

- [ ] **Step 3: Create `Desktop/App.axaml.cs`**

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GenieClient.Genie;

namespace GenieClient.Desktop;

public class App : Application
{
    IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddSingleton<Globals>()
            .AddSingleton<Game>(sp =>
            {
                var globals = sp.GetRequiredService<Globals>();
                return new Game(ref globals);
            })
            .AddSingleton<MainWindow>();
        _host = builder.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += async (_, _) => await _host.StopAsync();
            await _host.StartAsync();
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

**Note on `Game(ref Globals)`:** `Game` has a constructor `public Game(ref Globals cl)`. DI containers can't pass `ref` parameters directly, so we construct `Game` manually using the `Globals` singleton from the DI container.

- [ ] **Step 4: Verify build**

```bash
dotnet build Desktop/Genie4.Desktop.csproj 2>&1 | grep -E "error" | head -20
```

Fix any compiler errors before committing.

- [ ] **Step 5: Commit**

```bash
git add Desktop/Program.cs Desktop/App.axaml Desktop/App.axaml.cs
git commit -m "feat: add Avalonia App entry point and application class"
```

---

### Task 6: Create placeholder MainWindow

**Files:**
- Create: `Desktop/MainWindow.axaml`
- Create: `Desktop/MainWindow.axaml.cs`

The window has three sections:
1. A connect bar at the top (account, password, character fields + Connect button)
2. A scrolling text output area in the middle
3. A command input field at the bottom

- [ ] **Step 1: Create `Desktop/MainWindow.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="GenieClient.Desktop.MainWindow"
        Title="Genie"
        Width="900" Height="600"
        MinWidth="600" MinHeight="400">

  <DockPanel>

    <!-- Connect bar -->
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="6" Margin="6,6,6,0">
      <TextBlock VerticalAlignment="Center" Text="Account:" />
      <TextBox x:Name="AccountBox" Width="140" />
      <TextBlock VerticalAlignment="Center" Text="Password:" />
      <TextBox x:Name="PasswordBox" Width="140" PasswordChar="•" />
      <TextBlock VerticalAlignment="Center" Text="Character:" />
      <TextBox x:Name="CharacterBox" Width="120" />
      <Button x:Name="ConnectButton" Content="Connect" Click="ConnectButton_Click" />
    </StackPanel>

    <!-- Command input -->
    <TextBox DockPanel.Dock="Bottom"
             x:Name="CommandBox"
             Margin="6,0,6,6"
             KeyDown="CommandBox_KeyDown"
             Watermark="Enter command..." />

    <!-- Text output -->
    <ScrollViewer x:Name="OutputScroll" Margin="6,6,6,0">
      <SelectableTextBlock x:Name="OutputText"
                           FontFamily="Courier New,Consolas,monospace"
                           FontSize="13"
                           TextWrapping="Wrap" />
    </ScrollViewer>

  </DockPanel>

</Window>
```

- [ ] **Step 2: Create `Desktop/MainWindow.axaml.cs`**

```csharp
using System;
using System.Drawing;
using Avalonia.Controls;
using Avalonia.Input;
using GenieClient.Genie;

namespace GenieClient.Desktop;

public partial class MainWindow : Window
{
    // Hard-coded game server defaults (matches Connection.cs defaults)
    private const string DefaultHost = "dr.simutronics.net";
    private const int DefaultPort = 11024;

    private readonly Game _game;

    public MainWindow(Game game)
    {
        InitializeComponent();
        _game = game;
        _game.EventPrintText += OnPrintText;
    }

    private void ConnectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var account = AccountBox.Text ?? string.Empty;
        var password = PasswordBox.Text ?? string.Empty;
        var character = CharacterBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(character))
        {
            AppendOutput("[Connect] Character name is required.");
            return;
        }

        AppendOutput($"[Connecting as {character} to {DefaultHost}:{DefaultPort}...]");
        // Use DirectConnect with hard-coded DR host/port (no Simutronics account auth required for key-based direct login)
        // Game.Connect(sGenieKey, sAccountName, sPassword, sCharacter, sGame) routes through eaccess.play.net
        // Game.DirectConnect(Character, Game, Host, Port) connects directly with a pre-obtained key — simpler for the foundation.
        // For full account login support, call Connect() and add a GenieKey field.
        _game.DirectConnect(character, "DR", DefaultHost, DefaultPort);
    }

    private void CommandBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = CommandBox.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                _game.SendText(text, bUserInput: true);
                CommandBox.Text = string.Empty;
            }
            e.Handled = true;
        }
    }

    private void OnPrintText(string text, Color color, Color bgcolor,
        Game.WindowTarget targetwindow, string targetwindowstring,
        bool mono, bool isprompt, bool isinput)
    {
        // Marshal to UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
    }

    private void AppendOutput(string text)
    {
        OutputText.Text = (OutputText.Text ?? string.Empty) + text + "\n";
        // Scroll to bottom — Avalonia 11 ScrollViewer has no ScrollToEnd() method.
        // Setting Offset.Y to double.MaxValue is clamped to the actual maximum by Avalonia.
        OutputScroll.Offset = new Avalonia.Vector(OutputScroll.Offset.X, double.MaxValue);
    }
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build Desktop/Genie4.Desktop.csproj 2>&1 | grep -E "error" | head -20
```

Expected: Build succeeded, 0 errors. Fix any errors before committing.

- [ ] **Step 4: Commit**

```bash
git add Desktop/MainWindow.axaml Desktop/MainWindow.axaml.cs
git commit -m "feat: add placeholder MainWindow with connect bar, output, and command input"
```

---

## Chunk 3: Solution Update + Verification

### Task 7: Add Desktop project to solution

**Files:**
- Modify: `Genie4.sln`

Add `Desktop/Genie4.Desktop.csproj` to the solution so `dotnet build Genie4.sln` builds all projects.

- [ ] **Step 1: Add project to solution**

Run from the repo root (where `Genie4.sln` is):
```bash
dotnet sln Genie4.sln add Desktop/Genie4.Desktop.csproj
```

Expected output: `Project 'Desktop/Genie4.Desktop.csproj' added to the solution.`

- [ ] **Step 2: Verify solution builds (Desktop project only — skip Plugins.vbproj)**

The Plugins VB project will fail on Mac. Build just the Desktop project:
```bash
dotnet build Desktop/Genie4.Desktop.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Verify Windows csproj still builds**

```bash
dotnet build Genie4.csproj -f net6.0-windows 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Genie4.sln
git commit -m "feat: add Genie4.Desktop to solution"
```

---

### Task 8: Fix any remaining build errors

This task is a sweep pass. Run a full Desktop build and fix any errors not covered by earlier tasks.

- [ ] **Step 1: Run full Desktop build**

```bash
dotnet build Desktop/Genie4.Desktop.csproj 2>&1 | head -60
```

- [ ] **Step 2: For each compiler error, apply the appropriate fix**

Common patterns and fixes:

| Error pattern | Fix |
|---|---|
| `type 'X' could not be found` | Add `#if !DESKTOP` guard around the field/type |
| `name 'X' does not exist` | Guard the call site with `#if !DESKTOP` |
| `CS0246` on a WinForms type | Check if the file should be excluded from csproj `<Compile>` |
| Ambiguous reference between namespaces | Add explicit namespace qualifier |

Rebuild after each fix. Do NOT use `git add -A` — stage only files you modified.

- [ ] **Step 3: Commit any fixes**

```bash
git add <file1> [<file2> ...]
git commit -m "feat: fix remaining Desktop build errors"
```

---

### Task 9: Smoke test — run Avalonia window

- [ ] **Step 1: Launch the Desktop app**

```bash
dotnet run --project Desktop/Genie4.Desktop.csproj
```

Expected: Avalonia window opens with connect bar, empty output area, and command input.

- [ ] **Step 2: Verify connect flow (optional — requires valid DR account)**

Enter account name, password, character name, click Connect. Expected: connection attempt visible in output area, game text begins appearing.

- [ ] **Step 3: Verify command input**

Type a command in the command box, press Enter. Expected: text clears from the box and appears in output prefixed with `>` (or however `Game.SendText` echoes it).

- [ ] **Step 4: Final commit if any loose changes remain**

```bash
git status
# If clean: nothing to do.
# If dirty:
git add <modified-files>
git commit -m "chore: final Desktop foundation cleanup"
```
