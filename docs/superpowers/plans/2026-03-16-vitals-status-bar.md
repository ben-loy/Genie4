# Vitals & Status Bar Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a three-row vitals/status bar to the Avalonia desktop client matching WinForms parity: a status row (RT | LH | RH | Spell | SpellTimer) above the command input, and a vitals row (Health · Mana · Concentration · Fatigue · Spirit) below it.

**Architecture:** Two new reusable `UserControl`s (`VitalBarControl`, `TimerBarControl`) are added to `Desktop/Controls/`. `MainWindow.axaml` is restructured to add the two new rows and reposition the existing `CommandBox`. `MainWindow.axaml.cs` expands existing event stubs and adds game-loop tick logic for the countdown bars.

**Tech Stack:** Avalonia 11.3, .NET 10, C#. No test framework present — verification is `dotnet build` from `Desktop/` after each chunk.

---

## File Structure

| File | Role |
|---|---|
| `Desktop/Controls/VitalBarControl.axaml` | AXAML for a single vital stat bar (fill + label overlay) |
| `Desktop/Controls/VitalBarControl.axaml.cs` | Properties: `Value`, `BarText`, `FillColor`, `EmptyColor`, `IsConnected` |
| `Desktop/Controls/TimerBarControl.axaml` | AXAML for a countdown bar (RT or SpellTimer) |
| `Desktop/Controls/TimerBarControl.axaml.cs` | Properties: `Remaining`, `Total`, `FillColor`, `IsConnected` |
| `Desktop/MainWindow.axaml` | Add `StatusRow` (3rd Bottom), reorder `CommandBox` (2nd Bottom), add `VitalsRow` (1st Bottom) |
| `Desktop/MainWindow.axaml.cs` | New fields, expand `OnVariableChanged`, replace event stubs, add `InitializeTimerColors()`, `InitializeVitalColors()`, game loop tick additions |

---

## Chunk 1: New UserControls — VitalBarControl and TimerBarControl

### Task 1: VitalBarControl AXAML

**Files:**
- Create: `Desktop/Controls/VitalBarControl.axaml`

- [ ] **Step 1: Create the Controls directory and VitalBarControl AXAML file**

```xml
<!-- Desktop/Controls/VitalBarControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="GenieClient.Desktop.Controls.VitalBarControl">
  <Grid x:Name="BarGrid">
    <Grid.ColumnDefinitions>
      <ColumnDefinition x:Name="FillColumn" Width="50*"/>
      <ColumnDefinition x:Name="EmptyColumn" Width="50*"/>
    </Grid.ColumnDefinitions>
    <Border x:Name="FillBorder"  Grid.Column="0"/>
    <Border x:Name="EmptyBorder" Grid.Column="1"/>
    <TextBlock x:Name="BarLabel"
               Grid.ColumnSpan="2"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               Foreground="White"
               FontSize="10"
               FontFamily="Courier New, Monospace"/>
  </Grid>
</UserControl>
```

- [ ] **Step 2: Build to verify AXAML parses**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

---

### Task 2: VitalBarControl Code-Behind

**Files:**
- Create: `Desktop/Controls/VitalBarControl.axaml.cs`

- [ ] **Step 1: Create the code-behind**

```csharp
// Desktop/Controls/VitalBarControl.axaml.cs
using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace GenieClient.Desktop.Controls;

public partial class VitalBarControl : UserControl
{
    private int    _value       = 100;
    private string _barText     = "";
    private IBrush _fillColor   = Brushes.DarkGray;
    private IBrush _emptyColor  = Brushes.Black;
    private bool   _isConnected = true;

    public VitalBarControl() => InitializeComponent();

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            FillColumn.Width  = new Avalonia.Controls.GridLength(_value,       Avalonia.Controls.GridUnitType.Star);
            EmptyColumn.Width = new Avalonia.Controls.GridLength(100 - _value, Avalonia.Controls.GridUnitType.Star);
            BarLabel.Text = _barText;
        }
    }

    public string BarText
    {
        get => _barText;
        set { _barText = value; BarLabel.Text = value; }
    }

    public IBrush FillColor
    {
        get => _fillColor;
        set { _fillColor = value; ApplyBrushes(); }
    }

    public IBrush EmptyColor
    {
        get => _emptyColor;
        set { _emptyColor = value; ApplyBrushes(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; ApplyBrushes(); }
    }

    private void ApplyBrushes()
    {
        if (_isConnected)
        {
            FillBorder.Background  = _fillColor;
            EmptyBorder.Background = _emptyColor;
        }
        else
        {
            FillBorder.Background  = Grayscale(_fillColor);
            EmptyBorder.Background = Grayscale(_emptyColor);
        }
    }

    private static IBrush Grayscale(IBrush brush)
    {
        if (brush is SolidColorBrush scb)
        {
            var c   = scb.Color;
            byte lum = (byte)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
            return new SolidColorBrush(Avalonia.Media.Color.FromRgb(lum, lum, lum));
        }
        return brush;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Desktop/Controls/VitalBarControl.axaml Desktop/Controls/VitalBarControl.axaml.cs
git commit -m "feat: add VitalBarControl UserControl for vital stat bars"
```

---

### Task 3: TimerBarControl AXAML

**Files:**
- Create: `Desktop/Controls/TimerBarControl.axaml`

- [ ] **Step 1: Create TimerBarControl AXAML**

```xml
<!-- Desktop/Controls/TimerBarControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="GenieClient.Desktop.Controls.TimerBarControl"
             Background="#000000">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition x:Name="FillColumn"  Width="0*"/>
      <ColumnDefinition x:Name="EmptyColumn" Width="1*"/>
    </Grid.ColumnDefinitions>
    <Border x:Name="FillBorder"  Grid.Column="0" Background="Transparent"/>
    <Border x:Name="EmptyBorder" Grid.Column="1" Background="Transparent"/>
    <TextBlock x:Name="TimerLabel"
               Grid.ColumnSpan="2"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               Foreground="White"
               FontSize="10"
               FontFamily="Courier New, Monospace"/>
  </Grid>
</UserControl>
```

- [ ] **Step 2: Build to verify AXAML parses**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

---

### Task 4: TimerBarControl Code-Behind

**Files:**
- Create: `Desktop/Controls/TimerBarControl.axaml.cs`

- [ ] **Step 1: Create the code-behind**

```csharp
// Desktop/Controls/TimerBarControl.axaml.cs
using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace GenieClient.Desktop.Controls;

public partial class TimerBarControl : UserControl
{
    private int    _remaining   = 0;
    private int    _total       = 0;
    private IBrush _fillColor   = Brushes.MediumBlue;
    private bool   _isConnected = true;

    public TimerBarControl() => InitializeComponent();

    public int Remaining
    {
        get => _remaining;
        set { _remaining = Math.Max(0, value); Refresh(); }
    }

    public int Total
    {
        get => _total;
        set { _total = Math.Max(0, value); Refresh(); }
    }

    public IBrush FillColor
    {
        get => _fillColor;
        set { _fillColor = value; ApplyBrushes(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; ApplyBrushes(); }
    }

    private void Refresh()
    {
        if (_total <= 0 || _remaining <= 0)
        {
            FillColumn.Width  = new Avalonia.Controls.GridLength(0, Avalonia.Controls.GridUnitType.Star);
            EmptyColumn.Width = new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
            TimerLabel.Text   = "";
            FillBorder.Background  = Brushes.Transparent;
            EmptyBorder.Background = Brushes.Transparent;
        }
        else
        {
            FillColumn.Width  = new Avalonia.Controls.GridLength(_remaining,         Avalonia.Controls.GridUnitType.Star);
            EmptyColumn.Width = new Avalonia.Controls.GridLength(_total - _remaining, Avalonia.Controls.GridUnitType.Star);
            TimerLabel.Text   = _remaining.ToString();
            ApplyBrushes();
        }
    }

    private void ApplyBrushes()
    {
        if (_remaining <= 0) return;
        FillBorder.Background  = _isConnected ? _fillColor : Grayscale(_fillColor);
        EmptyBorder.Background = Brushes.Transparent;
    }

    private static IBrush Grayscale(IBrush brush)
    {
        if (brush is SolidColorBrush scb)
        {
            var c   = scb.Color;
            byte lum = (byte)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
            return new SolidColorBrush(Avalonia.Media.Color.FromRgb(lum, lum, lum));
        }
        return brush;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Desktop/Controls/TimerBarControl.axaml Desktop/Controls/TimerBarControl.axaml.cs
git commit -m "feat: add TimerBarControl UserControl for RT and SpellTimer countdown bars"
```

---

## Chunk 2: MainWindow.axaml — Layout Restructure

### Task 5: Add StatusRow, reorder CommandBox, add VitalsRow

**Files:**
- Modify: `Desktop/MainWindow.axaml`

- [ ] **Step 1: Add the `controls` XML namespace to the Window element**

Find the opening `<Window` tag in `Desktop/MainWindow.axaml`. It currently ends around:
```xml
        Title="Genie"
        Width="900" Height="600"
        MinWidth="600" MinHeight="400">
```

Add one line — the `xmlns:controls` attribute — so the tag reads:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:GenieClient.Desktop.Controls"
        x:Class="GenieClient.Desktop.MainWindow"
        Title="Genie"
        Width="900" Height="600"
        MinWidth="600" MinHeight="400">
```

- [ ] **Step 2: Replace the CommandBox + DockGrid block**

Find and replace this existing block at the bottom of the `<DockPanel>`:
```xml
    <!-- Command input -->
    <TextBox DockPanel.Dock="Bottom"
             x:Name="CommandBox"
             Margin="6,0,6,6"
             KeyDown="CommandBox_KeyDown"
             Watermark="Enter command..." />

    <!-- Dock area: DockManager adds columns and panels programmatically -->
    <Grid x:Name="DockGrid" Margin="6,6,6,0" />
```

Replace with:
```xml
    <!-- Vitals row — very bottom (declared first Bottom-docked) -->
    <!-- NOTE: FillColor and EmptyColor are plain CLR IBrush properties (not AvaloniaProperty),
         so they cannot be set from AXAML hex strings. Colors are set from code-behind
         in InitializeVitalColors() after InitializeComponent(). -->
    <Grid DockPanel.Dock="Bottom" x:Name="VitalsRow" Height="22" Background="#000000">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <controls:VitalBarControl Grid.Column="0" x:Name="VitalsHealth"  BarText="Health"/>
      <controls:VitalBarControl Grid.Column="1" x:Name="VitalsMana"    BarText="Mana"/>
      <controls:VitalBarControl Grid.Column="2" x:Name="VitalsConc"    BarText="Concentration"/>
      <controls:VitalBarControl Grid.Column="3" x:Name="VitalsFatigue" BarText="Fatigue"/>
      <controls:VitalBarControl Grid.Column="4" x:Name="VitalsSpirit"  BarText="Spirit"/>
    </Grid>

    <!-- Command input — above vitals (declared second Bottom-docked) -->
    <TextBox DockPanel.Dock="Bottom"
             x:Name="CommandBox"
             Margin="6,0,6,6"
             KeyDown="CommandBox_KeyDown"
             Watermark="Enter command..." />

    <!-- Status row — above command input (declared third Bottom-docked) -->
    <Grid DockPanel.Dock="Bottom" x:Name="StatusRow" Height="22" Background="#111111">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="72"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <!-- RT countdown bar -->
      <Border Grid.Column="0" BorderThickness="0,0,1,0" BorderBrush="#2A2A2A">
        <controls:TimerBarControl x:Name="RtBar"/>
      </Border>
      <!-- Left hand -->
      <Border Grid.Column="1" BorderThickness="0,0,1,0" BorderBrush="#2A2A2A" Padding="4,0">
        <TextBlock x:Name="LabelLH" VerticalAlignment="Center"
                   Foreground="#CCCCCC" FontSize="10" FontFamily="Courier New, Monospace"/>
      </Border>
      <!-- Right hand -->
      <Border Grid.Column="2" BorderThickness="0,0,1,0" BorderBrush="#2A2A2A" Padding="4,0">
        <TextBlock x:Name="LabelRH" VerticalAlignment="Center"
                   Foreground="#CCCCCC" FontSize="10" FontFamily="Courier New, Monospace"/>
      </Border>
      <!-- Spell + elapsed -->
      <Border Grid.Column="3" BorderThickness="0,0,1,0" BorderBrush="#2A2A2A" Padding="4,0">
        <TextBlock x:Name="LabelSpell" VerticalAlignment="Center"
                   Foreground="#CCCCCC" FontSize="10" FontFamily="Courier New, Monospace"/>
      </Border>
      <!-- SpellTimer countdown bar -->
      <controls:TimerBarControl Grid.Column="4" x:Name="SpellTimerBar"/>
    </Grid>

    <!-- Dock area: DockManager adds columns and panels programmatically -->
    <Grid x:Name="DockGrid" Margin="6,6,6,0" />
```

- [ ] **Step 3: Build to verify AXAML compiles**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors. The controls appear in the layout (VitalBarControl and TimerBarControl are referenced by x:Name and code-behind will compile).

- [ ] **Step 4: Commit**

```bash
git add Desktop/MainWindow.axaml
git commit -m "feat: add status row and vitals row to MainWindow layout"
```

---

## Chunk 3: MainWindow.axaml.cs — Event Wiring and Game Loop

### Task 6: Add new fields and InitializeTimerColors

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

- [ ] **Step 1: Add new fields after the existing `_gameLoopTimer` field**

Find the line:
```csharp
    private DispatcherTimer? _gameLoopTimer;
```

Add after it:
```csharp
    // Vitals & status bar fields
    private int _rtStart   = 0;   // Starting RT value for fill proportion
    private int _castTotal = 0;   // casttime - gametime, for SpellTimer fill proportion
```

- [ ] **Step 2: Add `InitializeVitalColors()` and `InitializeTimerColors()` methods**

Add both methods anywhere in the class (e.g., near the other `Initialize*` helpers).

`InitializeVitalColors()` sets the hardcoded WinForms-matching colors on the five vital bars. Colors are plain CLR `IBrush` properties and cannot be set from AXAML hex strings, so they are set here from code-behind after `InitializeComponent()`.

`InitializeTimerColors()` reads from the game's `PresetList` after presets are loaded. `Presets` has a typed `Preset this[string key]` indexer — no cast required. Use `ContainsKey` (not `Contains`) to check key existence; `SortedList.Contains` checks *values* not keys.

```csharp
    private static Avalonia.Media.IBrush AvaloniaColor(System.Drawing.Color c) =>
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(c.R, c.G, c.B));

    private void InitializeVitalColors()
    {
        // Hardcoded WinForms-matching colors — set from code-behind because FillColor/EmptyColor
        // are plain CLR IBrush properties (not AvaloniaProperty) and cannot be set from AXAML.
        VitalsHealth.FillColor  = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x80, 0x00, 0x00));
        VitalsHealth.EmptyColor = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x40, 0x00, 0x00));
        VitalsMana.FillColor    = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x00, 0x00, 0x80));
        VitalsMana.EmptyColor   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x00, 0x00, 0x40));
        VitalsConc.FillColor    = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x00, 0x80, 0x80));
        VitalsConc.EmptyColor   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x00, 0x40, 0x40));
        VitalsFatigue.FillColor  = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x00, 0x80, 0x00));
        VitalsFatigue.EmptyColor = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x00, 0x40, 0x00));
        VitalsSpirit.FillColor   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x80, 0x00, 0x80));
        VitalsSpirit.EmptyColor  = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x40, 0x00, 0x40));
    }

    private void InitializeTimerColors()
    {
        // Presets.Preset indexer already returns a typed Preset — no cast needed.
        // Use ContainsKey (not Contains — SortedList.Contains checks values, not keys).
        if (_game.Globals.PresetList.ContainsKey("roundtime"))
            RtBar.FillColor = AvaloniaColor(_game.Globals.PresetList["roundtime"].FgColor);

        if (_game.Globals.PresetList.ContainsKey("castbar"))
            SpellTimerBar.FillColor = AvaloniaColor(_game.Globals.PresetList["castbar"].FgColor);
    }
```

- [ ] **Step 3: Call both methods from the right locations**

`InitializeVitalColors()` must run on the UI thread right after `InitializeComponent()` returns in the `MainWindow` constructor. Find the constructor and add the call after `InitializeComponent()`:
```csharp
        InitializeComponent();
        InitializeVitalColors();  // ← add this line
```

`InitializeTimerColors()` must run after presets are loaded (inside `InitializeAsync()`). Add it after `StartGameLoopTimer()`:
```csharp
        StartGameLoopTimer();
        InitializeTimerColors();  // ← add this line
```

- [ ] **Step 4: Build to verify**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

---

### Task 7: Expand OnVariableChanged

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs`

- [ ] **Step 1: Replace the `OnVariableChanged` method body**

Find the existing method:
```csharp
    private void OnVariableChanged(string variable)
    {
        if (variable is "$gamename" or "$connected" or "$charactername")
            UpdateWindowTitle();
    }
```

Replace with:
```csharp
    private void OnVariableChanged(string variable)
    {
        // Preserve existing title update
        if (variable is "$gamename" or "$connected" or "$charactername")
            UpdateWindowTitle();

        // Vitals and status bar updates (all run on UI thread via Dispatcher)
        Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateFromVariable(variable));
    }

    private void UpdateFromVariable(string variable)
    {
        var vl = _game.Globals.VariableList;
        switch (variable)
        {
            case "$health":
                if (int.TryParse(vl["health"]?.ToString(), out int hp))
                    VitalsHealth.Value = hp;
                VitalsHealth.BarText = vl["healthBarText"]?.ToString() ?? "Health";
                break;
            case "$mana":
                if (int.TryParse(vl["mana"]?.ToString(), out int mp))
                    VitalsMana.Value = mp;
                VitalsMana.BarText = vl["manaBarText"]?.ToString() ?? "Mana";
                break;
            case "$spirit":
                if (int.TryParse(vl["spirit"]?.ToString(), out int sp))
                    VitalsSpirit.Value = sp;
                VitalsSpirit.BarText = vl["spiritBarText"]?.ToString() ?? "Spirit";
                break;
            case "$stamina":
                if (int.TryParse(vl["stamina"]?.ToString(), out int st))
                    VitalsFatigue.Value = st;
                VitalsFatigue.BarText = vl["staminaBarText"]?.ToString() ?? "Fatigue";
                break;
            case "$concentration":
                if (int.TryParse(vl["concentration"]?.ToString(), out int cn))
                    VitalsConc.Value = cn;
                VitalsConc.BarText = vl["concentrationBarText"]?.ToString() ?? "Concentration";
                break;
            case "$lefthand":
                LabelLH.Text = "L  " + (vl["lefthand"]?.ToString() ?? "");
                break;
            case "$righthand":
                LabelRH.Text = "R  " + (vl["righthand"]?.ToString() ?? "");
                break;
            case "$preparedspell":
                UpdateSpellLabel();
                break;
            case "$connected":
                bool isConn = vl["connected"]?.ToString() == "1";
                VitalsHealth.IsConnected = isConn;
                VitalsMana.IsConnected   = isConn;
                VitalsConc.IsConnected   = isConn;
                VitalsFatigue.IsConnected = isConn;
                VitalsSpirit.IsConnected  = isConn;
                RtBar.IsConnected        = isConn;
                SpellTimerBar.IsConnected = isConn;
                break;
        }
    }

    private void UpdateSpellLabel()
    {
        var vl     = _game.Globals.VariableList;
        var spell  = vl["preparedspell"]?.ToString() ?? "";
        var start  = _game.Globals.SpellTimeStart;
        bool showTimer = _game.Globals.Config.bShowSpellTimer
                         && start != DateTime.MinValue
                         && spell != "None"
                         && spell != "";
        if (showTimer)
        {
            int elapsed = (int)(DateTime.Now - start).TotalSeconds;
            LabelSpell.Text = $"({elapsed}) {spell}";
        }
        else
        {
            LabelSpell.Text = spell;
        }
    }

    private void UpdateStatusLabels()
    {
        var vl = _game.Globals.VariableList;
        LabelLH.Text = "L  " + (vl["lefthand"]?.ToString()  ?? "");
        LabelRH.Text = "R  " + (vl["righthand"]?.ToString() ?? "");
        UpdateSpellLabel();
    }
```

- [ ] **Step 2: Build to verify**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

---

### Task 8: Replace event stubs — RoundTime, CastTime, SpellTime, StatusBarUpdate

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs` (inside `WireGameEvents()`)

- [ ] **Step 1: Replace the four event stubs**

Find this block inside `WireGameEvents()`:
```csharp
        _game.EventRoundTime       += (_) => { /* TODO: roundtime countdown bar */ };
        _game.EventCastTime        += ()  => { /* TODO: cast timer bar */ };
        _game.EventSpellTime       += ()  => { /* TODO: active spell timer */ };
        _game.EventClearSpellTime  += ()  => { /* TODO: clear spell timer display */ };
        ...
        _game.EventStatusBarUpdate += ()  => { /* TODO: vitals bars */ };
```

Replace each stub with a real handler (leave any other stubs untouched):

```csharp
        _game.EventRoundTime += (iTime) =>
        {
            _rtStart = (int)(iTime + _game.Globals.Config.dRTOffset);
            _game.Globals.RoundTimeEnd = DateTime.Now.AddMilliseconds(
                iTime * 1000 + _game.Globals.Config.dRTOffset * 1000);
        };

        _game.EventCastTime += () =>
        {
            var vl = _game.Globals.VariableList;
            if (int.TryParse(vl["gametime"]?.ToString(),  out int gameTime) &&
                int.TryParse(vl["casttime"]?.ToString(),  out int castTime) &&
                vl["preparedspell"]?.ToString() != "None")
            {
                _castTotal = castTime - gameTime;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SpellTimerBar.Total     = _castTotal;
                    SpellTimerBar.Remaining = _castTotal;
                });
            }
            else
            {
                _castTotal = 0;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    SpellTimerBar.Remaining = 0);
            }
        };

        _game.EventSpellTime += () =>
            _game.Globals.SpellTimeStart = DateTime.Now;

        _game.EventClearSpellTime += () =>
        {
            _game.Globals.SpellTimeStart = DateTime.MinValue;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SpellTimerBar.Remaining = 0;
                _castTotal = 0;
                LabelSpell.Text = _game.Globals.VariableList["preparedspell"]?.ToString() ?? "";
            });
        };

        _game.EventStatusBarUpdate += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStatusLabels);
```

- [ ] **Step 2: Build to verify**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Desktop/MainWindow.axaml.cs
git commit -m "feat: wire RT, CastTime, SpellTime, StatusBarUpdate events to vitals bar controls"
```

---

### Task 9: Game Loop Tick — RT and SpellTimer countdown updates

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs` (inside `OnGameLoopTick()`)

- [ ] **Step 1: Add RT and SpellTimer updates to `OnGameLoopTick`**

Find the `OnGameLoopTick` method. It currently polls events, command queue, and calls script tick methods. Add these lines at the **end** of the method, before the closing brace:

```csharp
            // RT countdown bar
            int rtRemaining = (int)Math.Max(0, Math.Ceiling(
                (_game.Globals.RoundTimeEnd - DateTime.Now).TotalSeconds));
            if (rtRemaining != RtBar.Remaining)
            {
                int r = rtRemaining, s = _rtStart;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RtBar.Total     = s;
                    RtBar.Remaining = r;
                });
            }

            // SpellTimer countdown bar + S-column elapsed
            if (_game.Globals.SpellTimeStart != DateTime.MinValue)
            {
                double elapsed  = (DateTime.Now - _game.Globals.SpellTimeStart).TotalSeconds;
                int castRemain  = _castTotal > 0
                    ? (int)Math.Max(0, Math.Ceiling(_castTotal - elapsed))
                    : 0;
                int spellElapsed = (int)elapsed;

                if (castRemain != SpellTimerBar.Remaining || spellElapsed != _lastSpellElapsed)
                {
                    _lastSpellElapsed = spellElapsed;
                    int cr = castRemain, ct = _castTotal;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        SpellTimerBar.Total     = ct;
                        SpellTimerBar.Remaining = cr;
                        UpdateSpellLabel();
                    });
                }
            }
```

- [ ] **Step 2: Add the `_lastSpellElapsed` field** (to avoid redundant UI posts)

Find the new fields block added in Task 6:
```csharp
    private int _rtStart   = 0;
    private int _castTotal = 0;
```

Add one more field:
```csharp
    private int _lastSpellElapsed = -1;
```

- [ ] **Step 3: Build to verify**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Desktop/MainWindow.axaml.cs
git commit -m "feat: update RT and SpellTimer bars from game loop tick"
```

---

### Task 10: Final build and smoke test

- [ ] **Step 1: Clean build of full solution**

```bash
cd Desktop && dotnet build -v q
```
Expected: Build succeeded, 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2: Run the app and verify the layout**

Launch `Desktop/` project. Before connecting:
- Status row visible above command input (height 22px, dark background)
- Vitals row visible below command input (height 22px, black background)
- All 5 vitals show initial 100% fill with correct labels
- RT and SpellTimer segments are blank (no fill, no label)

After connecting and logging in:
- Health/Mana/Concentration/Fatigue/Spirit bars update as the server sends `<progressBar>` XML
- LH/RH labels update when items are picked up/dropped
- RT bar fills MediumBlue and counts down when RT occurs, goes blank at 0
- S column shows `(N) SpellName` counting up while spell is loaded; clears when unloaded
- SpellTimer bar fills Magenta and counts down during cast; goes blank when cast completes

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: vitals and status bar complete — WinForms parity for Avalonia UI"
```
