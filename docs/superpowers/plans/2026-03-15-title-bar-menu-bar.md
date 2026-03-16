# Title Bar and Menu Bar Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dynamic window title and a full menu bar (with stub handlers) to the Avalonia Desktop client, matching the structure of the Windows version.

**Architecture:** Expose `GameName` and `CharacterName` from `Core/Game.cs` (reading from `VariableList`, matching the Windows `FormMain` approach) so the Desktop can access server-assigned values. Subscribe to `EventVariableChanged` and `EventDisconnected` in `MainWindow.axaml.cs` to keep the title current. (`Game` has no public `EventConnected` — `EventVariableChanged` firing for `$connected` is the correct substitute.) Declare the full menu hierarchy in `MainWindow.axaml` with click handlers wired to empty stubs in `MainWindow.axaml.cs`.

**Tech Stack:** C# / Avalonia 11, .NET 8, `dotnet build` for verification (no test project exists in this repo).

---

## File Map

| File | Change |
|------|--------|
| `Core/Game.cs` | Add `public string GameName` and `public string CharacterName` properties |
| `Desktop/MainWindow.axaml` | Add `Menu` docked to top; full item hierarchy |
| `Desktop/MainWindow.axaml.cs` | Add `UpdateWindowTitle()`, event subscriptions, checkable fields, all stub handlers |

---

## Chunk 1: GameName + CharacterName Properties + Window Title

### Task 1: Expose `GameName` and `CharacterName` on `Game`

**Files:**
- Modify: `Core/Game.cs` (near the `AccountGame` property, around line 379)

- [ ] **Step 1: Add the two properties**

  Open `Core/Game.cs`. Find the `AccountGame` property block (around line 379). Add immediately after `AccountGame`:

  ```csharp
  /// <summary>
  /// The server-assigned game name (e.g. "DragonRealms"), set from XML during login.
  /// Equivalent to VariableList["gamename"]. Empty string until the server sends it.
  /// </summary>
  public string GameName => m_sGameName;

  /// <summary>
  /// The in-game character name from VariableList["charactername"].
  /// Set at connect time and may be updated by the server during the session.
  /// </summary>
  public string CharacterName =>
      m_oGlobals?.VariableList["charactername"]?.ToString() ?? string.Empty;
  ```

- [ ] **Step 2: Build to verify**

  ```bash
  cd /Users/bloy/src/Genie4/worktrees/title-bar
  dotnet build Genie4.sln
  ```

  Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

  ```bash
  git add Core/Game.cs
  git commit -m "feat: expose GameName and CharacterName properties on Game"
  ```

---

### Task 2: Add `UpdateWindowTitle` and event subscriptions

**Depends on:** Task 1 (uses `_game.GameName` and `_game.CharacterName`).

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

- [ ] **Step 1: Add `using` for `Reflection`**

  In `Desktop/MainWindow.axaml.cs`, add to the using block at the top:

  ```csharp
  using System.Reflection;
  ```

- [ ] **Step 2: Subscribe to `EventVariableChanged` in constructor**

  In the constructor, after `_game.EventPrintError += OnPrintError;`, add:

  ```csharp
  _game.EventVariableChanged += OnVariableChanged;
  ```

  > **Note:** `Game` exposes no public `EventConnected`. The correct substitute is `EventVariableChanged` filtering for `"$connected"`, which is fired by `VariableChanged("$connected")` inside the internal `GameSocket_EventConnected` handler.

- [ ] **Step 3: Call `UpdateWindowTitle()` at end of constructor**

  Still in the constructor, after all event subscriptions, add:

  ```csharp
  UpdateWindowTitle();
  ```

- [ ] **Step 4: Add `OnVariableChanged` handler**

  Add this private method after `OnPrintError`:

  ```csharp
  private void OnVariableChanged(string variable)
  {
      if (variable is "$gamename" or "$connected" or "$charactername")
          UpdateWindowTitle();
  }
  ```

- [ ] **Step 5: Update `OnDisconnected` to also refresh the title**

  Change the existing `OnDisconnected` method from:

  ```csharp
  private void OnDisconnected()
  {
      Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
  }
  ```

  To:

  ```csharp
  private void OnDisconnected()
  {
      Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
      UpdateWindowTitle();
  }
  ```

- [ ] **Step 6: Add `UpdateWindowTitle` method**

  `UpdateWindowTitle` may be called from any thread; it dispatches to the UI thread internally.

  Add after `OnDisconnected`:

  ```csharp
  private void UpdateWindowTitle()
  {
      var gameName = _game.GameName;
      var charName = _game.CharacterName;
      var connected = _game.IsConnected;
      var version = Assembly.GetExecutingAssembly().GetName().Version;
      Avalonia.Threading.Dispatcher.UIThread.Post(() =>
      {
          var sb = new System.Text.StringBuilder();
          if (!string.IsNullOrEmpty(gameName))
              sb.Append(gameName).Append(": ");
          if (!string.IsNullOrEmpty(charName))
              sb.Append(charName).Append(' ');
          sb.Append(connected ? "[Connected]" : "[Not connected]");
          sb.Append(" - Genie ").Append(version);
          Title = sb.ToString();
      });
  }
  ```

- [ ] **Step 7: Build to verify**

  ```bash
  cd /Users/bloy/src/Genie4/worktrees/title-bar
  dotnet build Genie4.sln
  ```

  Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

  ```bash
  git add Desktop/MainWindow.axaml.cs
  git commit -m "feat: add dynamic window title to Avalonia desktop"
  ```

---

## Chunk 2: Menu Bar XAML + Stub Handlers

> **Note:** Tasks 3 and 4 must be done together before building — the XAML Click handler references in Task 3 will cause build errors until the corresponding methods in Task 4 exist. Do not attempt to build between Task 3 and Task 4.

### Task 3: Add menu bar XAML

**Files:**
- Modify: `Desktop/MainWindow.axaml`

- [ ] **Step 1: Add the `Menu` control**

  In `MainWindow.axaml`, add the following immediately after `<DockPanel>` and before the `<!-- Connect bar -->` comment:

  ```xml
  <!-- Menu bar -->
  <Menu DockPanel.Dock="Top">

    <!-- File -->
    <MenuItem Header="_File">
      <MenuItem Header="Connect..." Click="MenuFile_Connect"/>
      <MenuItem Header="Connect Using Profile..." Click="MenuFile_ConnectUsingProfile"/>
      <Separator/>
      <MenuItem Header="Open Directory">
        <MenuItem Header="Genie"   Click="MenuDir_Genie"/>
        <MenuItem Header="Scripts" Click="MenuDir_Scripts"/>
        <MenuItem Header="Maps"    Click="MenuDir_Maps"/>
        <MenuItem Header="Plugins" Click="MenuDir_Plugins"/>
        <MenuItem Header="Logs"    Click="MenuDir_Logs"/>
        <MenuItem Header="Art"     Click="MenuDir_Art"/>
      </MenuItem>
      <Separator/>
      <MenuItem Header="Auto Log"            Click="MenuFile_AutoLog"/>
      <MenuItem Header="Open Log In Editor"  Click="MenuFile_OpenLogInEditor"/>
      <Separator/>
      <MenuItem Header="Auto Reconnect"            Click="MenuFile_AutoReconnect"/>
      <MenuItem Header="Classic Connect Window"    Click="MenuFile_ClassicConnectWindow"/>
      <MenuItem Header="Ignores/Gags Enabled"      Click="MenuFile_IgnoresEnabled"/>
      <MenuItem Header="Triggers Enabled"          Click="MenuFile_TriggersEnabled"/>
      <MenuItem Header="Plugins Enabled"           Click="MenuFile_PluginsEnabled"/>
      <MenuItem Header="AutoMapper Enabled"        Click="MenuFile_AutoMapperEnabled"/>
      <MenuItem Header="Images Enabled"            Click="MenuFile_ImagesEnabled"/>
      <MenuItem Header="Mute Sounds"               Click="MenuFile_MuteSounds"/>
      <Separator/>
      <MenuItem Header="Show Raw Data"          Click="MenuFile_ShowRawData"/>
      <MenuItem Header="Performance Test Parse" Click="MenuFile_PerformanceTestParse"/>
      <Separator/>
      <MenuItem Header="Exit" Click="MenuFile_Exit"/>
    </MenuItem>

    <!-- Edit -->
    <MenuItem Header="_Edit">
      <MenuItem Header="Paste Multi Line" Click="MenuEdit_PasteMultiLine"/>
      <Separator/>
      <MenuItem Header="Configuration..." Click="MenuEdit_Configuration"/>
      <MenuItem Header="Update Images"    Click="MenuEdit_UpdateImages"/>
    </MenuItem>

    <!-- Profile -->
    <MenuItem Header="_Profile">
      <MenuItem Header="Load Profile..." Click="MenuProfile_Load"/>
      <MenuItem Header="Save Profile"    Click="MenuProfile_Save"/>
      <Separator/>
      <MenuItem Header="Include Password In Profile" Click="MenuProfile_IncludePassword"/>
    </MenuItem>

    <!-- Layout -->
    <MenuItem Header="_Layout">
      <MenuItem Header="Load Layout..."       Click="MenuLayout_Load"/>
      <MenuItem Header="Load Default Layout"  Click="MenuLayout_LoadDefault"/>
      <Separator/>
      <MenuItem Header="Save Layout As..."         Click="MenuLayout_SaveAs"/>
      <MenuItem Header="Save Default Layout"       Click="MenuLayout_SaveDefault"/>
      <MenuItem Header="Save Sized Default Layout" Click="MenuLayout_SaveSizedDefault"/>
      <Separator/>
      <MenuItem Header="Basic Layout" Click="MenuLayout_Basic"/>
      <MenuItem Header="Icon Bar">
        <MenuItem Header="Dock Top"    Click="MenuLayout_IconBarDockTop"/>
        <MenuItem Header="Dock Bottom" Click="MenuLayout_IconBarDockBottom"/>
      </MenuItem>
      <MenuItem Header="Script Bar">
        <MenuItem Header="Dock Top"    Click="MenuLayout_ScriptBarDockTop"/>
        <MenuItem Header="Dock Bottom" Click="MenuLayout_ScriptBarDockBottom"/>
      </MenuItem>
      <MenuItem Header="Health Bar">
        <MenuItem Header="Dock Top"    Click="MenuLayout_HealthBarDockTop"/>
        <MenuItem Header="Dock Bottom" Click="MenuLayout_HealthBarDockBottom"/>
      </MenuItem>
      <MenuItem Header="Magic Panels" Click="MenuLayout_MagicPanels"/>
      <MenuItem Header="Status Bar"   Click="MenuLayout_StatusBar"/>
      <Separator/>
      <MenuItem Header="Align Input to Game Window" Click="MenuLayout_AlignInput"/>
      <MenuItem Header="Always On Top"              Click="MenuLayout_AlwaysOnTop"/>
    </MenuItem>

    <!-- Windows -->
    <MenuItem Header="_Windows">
      <!-- populated dynamically in a future sub-window feature -->
    </MenuItem>

    <!-- Script -->
    <MenuItem Header="_Script">
      <MenuItem Header="Script Explorer..."       Click="MenuScript_Explorer"/>
      <MenuItem Header="Update Scripts"           Click="MenuScript_UpdateScripts"/>
      <MenuItem Header="Update Scripts With Maps" Click="MenuScript_UpdateWithMaps"/>
      <Separator/>
      <MenuItem Header="Show Active Scripts"  Click="MenuScript_ShowActive"/>
      <MenuItem Header="Trace Active Scripts" Click="MenuScript_TraceActive"/>
      <Separator/>
      <MenuItem Header="Pause All Scripts"  Click="MenuScript_PauseAll"/>
      <MenuItem Header="Resume All Scripts" Click="MenuScript_ResumeAll"/>
      <Separator/>
      <MenuItem Header="Abort All Scripts" Click="MenuScript_AbortAll"/>
      <MenuItem Header="Script Settings"   Click="MenuScript_Settings"/>
    </MenuItem>

    <!-- AutoMapper -->
    <MenuItem Header="_AutoMapper">
      <MenuItem Header="Show Window"    Click="MenuAutoMapper_ShowWindow"/>
      <MenuItem Header="Update Maps"    Click="MenuAutoMapper_UpdateMaps"/>
      <MenuItem Header="Script Settings" Click="MenuAutoMapper_Settings"/>
    </MenuItem>

    <!-- Plugins -->
    <MenuItem Header="P_lugins">
      <MenuItem x:Name="_MenuPluginsNoPlugins" Header="No plugins loaded"/>
      <MenuItem Header="Update Plugins" Click="MenuPlugins_Update"/>
    </MenuItem>

    <!-- Help -->
    <MenuItem Header="_Help">
      <MenuItem Header="Check For Updates" Click="MenuHelp_CheckForUpdates"/>
      <MenuItem Header="Force Update"      Click="MenuHelp_ForceUpdate"/>
      <MenuItem Header="Load Test Client"  Click="MenuHelp_LoadTestClient"/>
      <Separator/>
      <MenuItem Header="AutoUpdate"               Click="MenuHelp_AutoUpdate"/>
      <MenuItem Header="AutoUpdate Lamp"          Click="MenuHelp_AutoUpdateLamp"/>
      <MenuItem Header="Check Updates on Startup" Click="MenuHelp_CheckUpdatesOnStartup"/>
      <Separator/>
      <MenuItem Header="Latest Release Page" Click="MenuHelp_LatestReleasePage"/>
      <Separator/>
      <MenuItem Header="Discord" Click="MenuHelp_Discord"/>
      <MenuItem Header="GitHub"  Click="MenuHelp_GitHub"/>
      <MenuItem Header="Wiki"    Click="MenuHelp_Wiki"/>
      <Separator/>
      <MenuItem Header="Community Links">
        <MenuItem Header="Play.net"                 Click="MenuCommunity_Playnet"/>
        <MenuItem Header="Elanthipedia"             Click="MenuCommunity_Elanthipedia"/>
        <MenuItem Header="DR Service"               Click="MenuCommunity_DRService"/>
        <MenuItem Header="Lich Discord"             Click="MenuCommunity_LichDiscord"/>
        <MenuItem Header="Isharon's Genie Settings" Click="MenuCommunity_IsharonSettings"/>
      </MenuItem>
    </MenuItem>

  </Menu>
  ```

---

### Task 4: Add stub handlers, checkable fields, and initialization

**Must be completed in the same session as Task 3 before building.**

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

- [ ] **Step 1: Add checkable state fields**

  Add these private fields to the class body (after `private readonly Game _game;`):

  ```csharp
  // Checkable menu state
  private bool _autoLog = false;
  private bool _autoReconnect = false;
  private bool _ignoresEnabled = true;
  private bool _triggersEnabled = true;
  private bool _pluginsEnabled = true;
  private bool _autoMapperEnabled = true;
  private bool _imagesEnabled = true;
  private bool _muteSounds = false;
  private bool _showRawData = false;
  private bool _alwaysOnTop = false;
  private bool _includePasswordInProfile = false;
  private bool _autoUpdate = true;
  private bool _autoUpdateLamp = false;
  private bool _checkUpdatesOnStartup = true;
  ```

- [ ] **Step 2: Disable "No plugins loaded" item in constructor**

  In the constructor, after `UpdateWindowTitle();` (added in Task 2 Step 3), add:

  ```csharp
  _MenuPluginsNoPlugins.IsEnabled = false;
  ```

- [ ] **Step 3: Add all stub handlers**

  Add the following block of methods at the end of the class (before the closing `}`):

  ```csharp
  // ── File ──────────────────────────────────────────────────────────────────
  private void MenuFile_Connect(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuFile_ConnectUsingProfile(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuDir_Genie(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuDir_Scripts(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuDir_Maps(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuDir_Plugins(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuDir_Logs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuDir_Art(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuFile_AutoLog(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _autoLog = !_autoLog; /* TODO */ }
  private void MenuFile_OpenLogInEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuFile_AutoReconnect(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _autoReconnect = !_autoReconnect; /* TODO */ }
  private void MenuFile_ClassicConnectWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuFile_IgnoresEnabled(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _ignoresEnabled = !_ignoresEnabled; /* TODO */ }
  private void MenuFile_TriggersEnabled(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _triggersEnabled = !_triggersEnabled; /* TODO */ }
  private void MenuFile_PluginsEnabled(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _pluginsEnabled = !_pluginsEnabled; /* TODO */ }
  private void MenuFile_AutoMapperEnabled(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _autoMapperEnabled = !_autoMapperEnabled; /* TODO */ }
  private void MenuFile_ImagesEnabled(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _imagesEnabled = !_imagesEnabled; /* TODO */ }
  private void MenuFile_MuteSounds(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _muteSounds = !_muteSounds; /* TODO */ }
  private void MenuFile_ShowRawData(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _showRawData = !_showRawData; /* TODO */ }
  private void MenuFile_PerformanceTestParse(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuFile_Exit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { Close(); }

  // ── Edit ──────────────────────────────────────────────────────────────────
  private void MenuEdit_PasteMultiLine(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuEdit_Configuration(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuEdit_UpdateImages(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }

  // ── Profile ───────────────────────────────────────────────────────────────
  private void MenuProfile_Load(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuProfile_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuProfile_IncludePassword(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _includePasswordInProfile = !_includePasswordInProfile; /* TODO */ }

  // ── Layout ────────────────────────────────────────────────────────────────
  private void MenuLayout_Load(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_LoadDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_SaveAs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_SaveDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_SaveSizedDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_Basic(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_IconBarDockTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_IconBarDockBottom(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_ScriptBarDockTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_ScriptBarDockBottom(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_HealthBarDockTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_HealthBarDockBottom(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_MagicPanels(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_StatusBar(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_AlignInput(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuLayout_AlwaysOnTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _alwaysOnTop = !_alwaysOnTop; /* TODO */ }

  // ── Script ────────────────────────────────────────────────────────────────
  private void MenuScript_Explorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_UpdateScripts(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_UpdateWithMaps(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_ShowActive(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_TraceActive(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_PauseAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_ResumeAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_AbortAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuScript_Settings(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }

  // ── AutoMapper ────────────────────────────────────────────────────────────
  private void MenuAutoMapper_ShowWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuAutoMapper_UpdateMaps(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuAutoMapper_Settings(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }

  // ── Plugins ───────────────────────────────────────────────────────────────
  private void MenuPlugins_Update(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }

  // ── Help ──────────────────────────────────────────────────────────────────
  private void MenuHelp_CheckForUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuHelp_ForceUpdate(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuHelp_LoadTestClient(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuHelp_AutoUpdate(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _autoUpdate = !_autoUpdate; /* TODO */ }
  private void MenuHelp_AutoUpdateLamp(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _autoUpdateLamp = !_autoUpdateLamp; /* TODO */ }
  private void MenuHelp_CheckUpdatesOnStartup(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { _checkUpdatesOnStartup = !_checkUpdatesOnStartup; /* TODO */ }
  private void MenuHelp_LatestReleasePage(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuHelp_Discord(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuHelp_GitHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuHelp_Wiki(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuCommunity_Playnet(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuCommunity_Elanthipedia(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuCommunity_DRService(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuCommunity_LichDiscord(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  private void MenuCommunity_IsharonSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
  ```

- [ ] **Step 4: Build to verify everything compiles**

  ```bash
  cd /Users/bloy/src/Genie4/worktrees/title-bar
  dotnet build Genie4.sln
  ```

  Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

  ```bash
  git add Desktop/MainWindow.axaml Desktop/MainWindow.axaml.cs
  git commit -m "feat: add menu bar stubs to Avalonia desktop"
  ```

---

## Final Verification

- [ ] **Run the app and verify visually**

  ```bash
  cd /Users/bloy/src/Genie4/worktrees/title-bar
  dotnet run --project Desktop/Genie4.Desktop.csproj
  ```

  Confirm:
  - Title bar shows `[Not connected] - Genie 4.0.2.9` (or current version) at startup
  - Menu bar is visible with all 8 top-level menus
  - Each menu opens and shows all items
  - All items are clickable without crashing
  - "No plugins loaded" is greyed out and unclickable

- [ ] **Push branch**

  ```bash
  git push -u origin feature/title-bar
  ```
