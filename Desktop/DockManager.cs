using System;
using System.Collections.Generic;
using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using GenieClient.Genie;
using Color = System.Drawing.Color;

namespace GenieClient.Desktop;

/// <summary>
/// Owns all GameOutputPanel instances. Manages the docked Grid, routes text,
/// handles float/dock/hide lifecycle, populates the Windows menu, and persists
/// layout to XML using GenieClient.Genie.XMLConfig (same format as Windows client).
/// </summary>
public class DockManager
{
    private readonly Grid _dockGrid;
    private readonly MenuItem _windowsMenu;

    // All panels, keyed by lowercase name.
    private readonly Dictionary<string, GameOutputPanel> _panels
        = new(StringComparer.OrdinalIgnoreCase);

    // Per-panel Grid tracking.
    private readonly Dictionary<string, PanelSlot> _slots
        = new(StringComparer.OrdinalIgnoreCase);

    // Windows menu items, one per panel.
    private readonly Dictionary<string, MenuItem> _menuItems
        = new(StringComparer.OrdinalIgnoreCase);

    // True when a panel was hidden while it was floating.
    // Used by SetVisible to decide whether to re-dock or re-float on show.
    // NOT persisted — hidden-while-floating panels re-show as docked after app restart.
    private readonly Dictionary<string, bool> _wasFloatingWhenHidden
        = new(StringComparer.OrdinalIgnoreCase);

    // Last known float positions for each panel, updated by FloatingWindow events.
    private readonly Dictionary<string, FloatState> _floatPositions
        = new(StringComparer.OrdinalIgnoreCase);

    // ── WindowTarget → panel name routing table ───────────────────────────────
    private static readonly Dictionary<Game.WindowTarget, string> TargetMap = new()
    {
        [Game.WindowTarget.Main]         = "main",
        [Game.WindowTarget.Unknown]      = "main",
        [Game.WindowTarget.Combat]       = "combat",
        [Game.WindowTarget.Portrait]     = "portrait",
        [Game.WindowTarget.Inv]          = "inv",
        [Game.WindowTarget.Familiar]     = "familiar",
        [Game.WindowTarget.Thoughts]     = "thoughts",
        [Game.WindowTarget.Logons]       = "logons",
        [Game.WindowTarget.Death]        = "death",
        [Game.WindowTarget.Room]         = "room",
        [Game.WindowTarget.Log]          = "log",
        [Game.WindowTarget.Raw]          = "raw",
        [Game.WindowTarget.Debug]        = "debug",
        [Game.WindowTarget.ActiveSpells] = "percwindow",
        // WindowTarget.Other is handled dynamically by targetName.
    };

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called immediately after MainWindow.InitializeComponent().
    /// Creates the Main panel eagerly; loads default layout as final step.
    /// </summary>
    public DockManager(Grid dockGrid, MenuItem windowsMenu)
    {
        _dockGrid = dockGrid;
        _windowsMenu = windowsMenu;

        GetOrCreate("main");   // Main panel always exists
        LoadDefaultLayout();   // Apply saved XML state (or default if file missing)
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Main panel's ScrollViewer; handed to CommandInputController for Ctrl+PageUp/Down.
    /// Available immediately after construction because Main is created eagerly.
    /// </summary>
    public ScrollViewer MainScrollViewer => _panels["main"].OutputScroll;

    /// <summary>
    /// Routes a game text event to the correct panel (creating it if needed).
    /// Must be called on the UI thread.
    /// </summary>
    public void Route(Game.WindowTarget target, string targetName,
                      string text, Color fg, Color bg)
    {
        string panelName;

        if (target == Game.WindowTarget.Other)
        {
            panelName = string.IsNullOrWhiteSpace(targetName) ? "main" : targetName.ToLower();
        }
        else if (TargetMap.TryGetValue(target, out var mapped))
        {
            panelName = mapped;
        }
        else
        {
            panelName = "main";
        }

        GetOrCreate(panelName).AppendText(text, fg, bg);
    }

    // ── Layout persistence (XML via XMLConfig) ────────────────────────────────

    private static string DefaultLayoutPath =>
        System.IO.Path.Combine(LocalDirectory.Path, "layout.xml");

    public void SaveDefaultLayout() => SaveLayout(DefaultLayoutPath);
    public void LoadDefaultLayout() => LoadLayout(DefaultLayoutPath);

    /// <summary>
    /// Saves the current layout to an XML file compatible with the Windows Genie layout format.
    /// Schema: Genie/Windows/Game for Main; Genie/Windows/Window1..N for sub-panels.
    /// </summary>
    public void SaveLayout(string path)
    {
        var cfg = new XMLConfig();
        cfg.LoadXml("<Genie><Windows></Windows></Genie>");

        // Calculate total star-width for SizeRatio using SavedWidth as fallback for collapsed columns.
        double totalStarWidth = 0;
        foreach (var (_, slot) in _slots)
        {
            double w = _dockGrid.ColumnDefinitions[slot.PanelColIdx].ActualWidth;
            if (w <= 0) w = slot.SavedWidth.Value;  // panel is floating or hidden
            totalStarWidth += w;
        }

        int subIndex = 0;

        foreach (var (name, panel) in _panels)
        {
            bool isMain = string.Equals(name, "main", StringComparison.OrdinalIgnoreCase);
            string elemPath = isMain
                ? "Genie/Windows/Game"
                : $"Genie/Windows/Window{++subIndex}";

            double sizeRatio = 0;
            if (_slots.TryGetValue(name, out var slot) && totalStarWidth > 0)
            {
                double w = _dockGrid.ColumnDefinitions[slot.PanelColIdx].ActualWidth;
                if (w <= 0) w = slot.SavedWidth.Value;  // panel is floating or hidden
                sizeRatio = w / totalStarWidth;
            }

            double floatLeft = 0, floatTop = 0, floatW = 0, floatH = 0;
            if (_floatPositions.TryGetValue(name, out var fp))
            {
                floatLeft = fp.X; floatTop = fp.Y;
                floatW = fp.Width; floatH = fp.Height;
            }

            cfg.SetValue(elemPath, "ID",         name);
            cfg.SetValue(elemPath, "Name",        name);
            cfg.SetValue(elemPath, "Visible",     (!panel.IsOutputHidden).ToString());
            cfg.SetValue(elemPath, "IsFloating",  panel.IsFloating.ToString());
            cfg.SetValue(elemPath, "SizeRatio",   sizeRatio.ToString("R"));
            cfg.SetValue(elemPath, "FloatLeft",   floatLeft.ToString("R"));
            cfg.SetValue(elemPath, "FloatTop",    floatTop.ToString("R"));
            cfg.SetValue(elemPath, "FloatWidth",  floatW.ToString("R"));
            cfg.SetValue(elemPath, "FloatHeight", floatH.ToString("R"));
        }

        cfg.SetValue("Genie/Windows", "WindowCount", subIndex.ToString());

        try { cfg.SaveToFile(path); }
        catch { /* best-effort save; ignore disk errors */ }
    }

    /// <summary>
    /// Loads layout from XML. Compatible with the Windows Genie layout format.
    /// Falls back to default (Main only, docked) if the file is missing or corrupt.
    /// </summary>
    public void LoadLayout(string path)
    {
        var cfg = new XMLConfig();
        if (!cfg.LoadFile(path)) return;

        // Load Main panel.
        ApplyPanelEntry(cfg, "Genie/Windows/Game");

        // Load sub-panels.
        int count = cfg.GetValue("Genie/Windows", "WindowCount", 0);
        for (int i = 1; i <= count; i++)
            ApplyPanelEntry(cfg, $"Genie/Windows/Window{i}");
    }

    // ── Float / Dock ──────────────────────────────────────────────────────────

    /// <summary>
    /// Tears a panel off from the dock into a standalone FloatingWindow.
    /// Called by GetOrCreate's FloatRequested subscription.
    /// </summary>
    private void Float(GameOutputPanel panel)
    {
        // Collapse docked position.
        if (_slots.TryGetValue(panel.WindowName, out var slot))
        {
            slot.SavedWidth = _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width;
            _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width = new GridLength(0);
            panel.IsVisible = false;
            if (slot.Splitter != null)
            {
                _dockGrid.ColumnDefinitions[slot.SplitterColIdx].Width = new GridLength(0);
                slot.Splitter.IsVisible = false;
            }
        }

        _wasFloatingWhenHidden[panel.WindowName] = false;

        var fw = new FloatingWindow(
            panel,
            dockAction: () => Dock(panel),
            hideAction: () => OnPanelHiddenWhileFloating(panel));

        // Apply last known float position, or 50px from origin as default.
        if (_floatPositions.TryGetValue(panel.WindowName, out var savedPos)
            && savedPos.Width > 0 && savedPos.Height > 0)
        {
            fw.Position = new PixelPoint((int)savedPos.X, (int)savedPos.Y);
            fw.Width    = savedPos.Width;
            fw.Height   = savedPos.Height;
        }
        else
        {
            fw.Position = new PixelPoint(50, 50);
        }

        // DockManager tracks position/size changes directly — not FloatingWindow.
        fw.PositionChanged += (_, _) => OnFloatPositionChanged(panel, fw);
        fw.SizeChanged     += (_, _) => OnFloatPositionChanged(panel, fw);

        fw.Show();

        if (_menuItems.TryGetValue(panel.WindowName, out var mi)) mi.IsChecked = true;
        SaveDefaultLayout();
    }

    private void OnFloatPositionChanged(GameOutputPanel panel, FloatingWindow fw)
    {
        _floatPositions[panel.WindowName] = new FloatState(
            fw.Position.X, fw.Position.Y,
            fw.ClientSize.Width, fw.ClientSize.Height);
        SaveDefaultLayout();
    }

    /// <summary>
    /// Restores a floating panel back to its docked column.
    /// Called by FloatingWindow via the dockAction closure.
    /// FloatingWindow closes itself after this returns.
    /// </summary>
    private void Dock(GameOutputPanel panel)
    {
        panel.IsFloating = false;

        if (_slots.TryGetValue(panel.WindowName, out var slot))
        {
            _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width = slot.SavedWidth;
            panel.IsVisible = true;
            if (slot.Splitter != null)
            {
                _dockGrid.ColumnDefinitions[slot.SplitterColIdx].Width = new GridLength(4);
                slot.Splitter.IsVisible = true;
            }
        }

        if (_menuItems.TryGetValue(panel.WindowName, out var mi)) mi.IsChecked = true;
        SaveDefaultLayout();
    }

    /// <summary>
    /// Called by FloatingWindow's hideAction when the window is closed/hidden (not docked).
    /// Records that this panel was floating when hidden so SetVisible can re-float it.
    /// </summary>
    private void OnPanelHiddenWhileFloating(GameOutputPanel panel)
    {
        _wasFloatingWhenHidden[panel.WindowName] = true;
        if (_menuItems.TryGetValue(panel.WindowName, out var mi)) mi.IsChecked = false;
        SaveDefaultLayout();
    }

    // ── Show / Hide ───────────────────────────────────────────────────────────

    private void SetVisible(GameOutputPanel panel, bool visible)
    {
        // Guard: already in desired state.
        if (visible == !panel.IsOutputHidden) return;

        if (visible)
        {
            panel.IsOutputHidden = false;

            bool wasFloating = _wasFloatingWhenHidden.TryGetValue(
                panel.WindowName, out var wf) && wf;

            if (wasFloating)
            {
                Float(panel);  // re-create FloatingWindow at last known position
            }
            else
            {
                if (_slots.TryGetValue(panel.WindowName, out var slot))
                {
                    _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width = slot.SavedWidth;
                    panel.IsVisible = true;
                    if (slot.Splitter != null)
                    {
                        _dockGrid.ColumnDefinitions[slot.SplitterColIdx].Width = new GridLength(4);
                        slot.Splitter.IsVisible = true;
                    }
                }
                if (_menuItems.TryGetValue(panel.WindowName, out var mi)) mi.IsChecked = true;
                SaveDefaultLayout();
            }
        }
        else // hide
        {
            panel.IsOutputHidden = true;

            if (_slots.TryGetValue(panel.WindowName, out var slot))
            {
                slot.SavedWidth = _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width;
                _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width = new GridLength(0);
                panel.IsVisible = false;
                if (slot.Splitter != null)
                {
                    _dockGrid.ColumnDefinitions[slot.SplitterColIdx].Width = new GridLength(0);
                    slot.Splitter.IsVisible = false;
                }
            }

            if (_menuItems.TryGetValue(panel.WindowName, out var mi)) mi.IsChecked = false;
            SaveDefaultLayout();
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private GameOutputPanel GetOrCreate(string name)
    {
        name = name.ToLower();
        if (_panels.TryGetValue(name, out var existing)) return existing;

        var panel = new GameOutputPanel(name);
        _panels[name] = panel;
        AddToDock(panel);
        AddWindowsMenuItem(panel);

        // DockManager handles CloseRequested for docked panels.
        // For floating panels, FloatingWindow.Closing runs first (sets IsOutputHidden=true),
        // then DockManager's handler fires — the guard (visible == !IsOutputHidden) returns early.
        panel.CloseRequested += (_, _) => SetVisible(panel, false);
        panel.FloatRequested += (_, _) => Float(panel);

        return panel;
    }

    private void AddToDock(GameOutputPanel panel)
    {
        bool isMain = string.Equals(panel.WindowName, "main", StringComparison.OrdinalIgnoreCase);

        int splitterColIdx = -1;
        GridSplitter? splitter = null;

        if (!isMain)
        {
            splitterColIdx = _dockGrid.ColumnDefinitions.Count;
            _dockGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4)));

            splitter = new GridSplitter
            {
                Width            = 4,
                ResizeDirection  = GridResizeDirection.Columns,
                ResizeBehavior   = GridResizeBehavior.PreviousAndNext,
            };
            Grid.SetColumn(splitter, splitterColIdx);
            _dockGrid.Children.Add(splitter);

            // Save on splitter drag-end (PointerReleased = Avalonia 11 drag-end signal).
            splitter.PointerReleased += (_, _) => SaveDefaultLayout();
        }

        int panelColIdx = _dockGrid.ColumnDefinitions.Count;
        var initialWidth = new GridLength(200, GridUnitType.Star);

        _dockGrid.ColumnDefinitions.Add(new ColumnDefinition(initialWidth));
        Grid.SetColumn(panel, panelColIdx);
        _dockGrid.Children.Add(panel);

        _slots[panel.WindowName] = new PanelSlot(panelColIdx, splitterColIdx, splitter, initialWidth);
    }

    private void AddWindowsMenuItem(GameOutputPanel panel)
    {
        bool isMain = string.Equals(panel.WindowName, "main", StringComparison.OrdinalIgnoreCase);

        var item = new MenuItem
        {
            Header      = panel.WindowName,
            ToggleType  = MenuItemToggleType.CheckBox,   // required for IsChecked to render a checkmark in Avalonia 11
            IsChecked   = true,
            IsEnabled   = !isMain,   // Main panel cannot be hidden
        };

        item.Click += (_, _) =>
        {
            if (!isMain)
                SetVisible(panel, panel.IsOutputHidden);  // toggle
        };

        _windowsMenu.Items.Add(item);
        _menuItems[panel.WindowName] = item;
    }

    /// <summary>
    /// Reads one panel entry from cfg and applies its saved state.
    /// Called for both "Genie/Windows/Game" (Main) and "Genie/Windows/Window{N}" (sub-panels).
    /// </summary>
    private void ApplyPanelEntry(XMLConfig cfg, string elemPath)
    {
        string id = cfg.GetValue(elemPath, "ID", string.Empty);
        if (string.IsNullOrWhiteSpace(id)) return;

        var panel = GetOrCreate(id);

        double sizeRatio = cfg.GetValue(elemPath, "SizeRatio", 0.0);
        bool   visible   = cfg.GetValue(elemPath, "Visible",   true);
        bool   floating  = cfg.GetValue(elemPath, "IsFloating", false);

        double floatLeft = cfg.GetValue(elemPath, "FloatLeft",   0.0);
        double floatTop  = cfg.GetValue(elemPath, "FloatTop",    0.0);
        double floatW    = cfg.GetValue(elemPath, "FloatWidth",  0.0);
        double floatH    = cfg.GetValue(elemPath, "FloatHeight", 0.0);

        // Apply star-size to docked column.
        if (_slots.TryGetValue(id, out var slot))
        {
            const double totalStars = 1000;
            double stars = sizeRatio * totalStars;
            bool isMain  = string.Equals(id, "main", StringComparison.OrdinalIgnoreCase);
            if (stars <= 0) stars = 200;

            var newWidth = new GridLength(stars, GridUnitType.Star);
            _dockGrid.ColumnDefinitions[slot.PanelColIdx].Width = newWidth;
            slot.SavedWidth = newWidth;
        }

        // Store float position if available.
        if (floatW > 0 && floatH > 0)
            _floatPositions[id] = new FloatState(floatLeft, floatTop, floatW, floatH);

        if (!visible)
        {
            panel.IsOutputHidden = true;
            if (_slots.TryGetValue(id, out var s))
            {
                _dockGrid.ColumnDefinitions[s.PanelColIdx].Width = new GridLength(0);
                panel.IsVisible = false;
                if (s.Splitter != null)
                {
                    _dockGrid.ColumnDefinitions[s.SplitterColIdx].Width = new GridLength(0);
                    s.Splitter.IsVisible = false;
                }
            }
            if (_menuItems.TryGetValue(id, out var mi)) mi.IsChecked = false;
        }
        else if (floating)
        {
            Float(panel);
        }
        else
        {
            panel.IsOutputHidden = false;
            panel.IsVisible = true;
            if (_slots.TryGetValue(id, out var s) && s.Splitter != null)
                s.Splitter.IsVisible = true;
            if (_menuItems.TryGetValue(id, out var mi)) mi.IsChecked = true;
        }
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private class PanelSlot
    {
        public int PanelColIdx;
        public int SplitterColIdx;      // -1 for Main
        public GridSplitter? Splitter;  // null for Main
        public GridLength SavedWidth;

        public PanelSlot(int panelColIdx, int splitterColIdx,
                         GridSplitter? splitter, GridLength savedWidth)
        {
            PanelColIdx    = panelColIdx;
            SplitterColIdx = splitterColIdx;
            Splitter       = splitter;
            SavedWidth     = savedWidth;
        }
    }

    private record FloatState(double X, double Y, double Width, double Height);
}
