using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using GenieClient.Genie;
using Color = System.Drawing.Color;

namespace GenieClient.Desktop;

public partial class MainWindow : Window
{
    private readonly Game _game;
    private readonly CommandInputController _controller;
    private readonly DockManager _dockManager;

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

    public MainWindow(Game game)
    {
        InitializeComponent();
        _game = game;

        // 1. DockManager FIRST — creates Main panel eagerly and loads XML layout.
        _dockManager = new DockManager(DockGrid, _WindowsMenu);

        // 2. CommandInputController needs MainScrollViewer (available after step 1).
        _controller = new CommandInputController(_game, CommandBox, _dockManager.MainScrollViewer);

        // 3. Wire text events AFTER _dockManager is assigned.
        _game.EventPrintText       += OnPrintText;
        _game.EventDisconnected    += OnDisconnected;
        _game.EventPrintError      += OnPrintError;
        _game.EventVariableChanged += OnVariableChanged;

        // 4. Save layout on window close.
        Closing += (_, _) => _dockManager.SaveDefaultLayout();

        UpdateWindowTitle();
        _MenuPluginsNoPlugins.IsEnabled = false;
    }

    private void ConnectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var account   = AccountBox.Text   ?? string.Empty;
        var password  = PasswordBox.Text  ?? string.Empty;
        var character = CharacterBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            _dockManager.Route(Game.WindowTarget.Main, string.Empty,
                "[Connect] Account and password are required.\n", default, default);
            return;
        }

        ConnectButton.IsEnabled = false;

        var statusMessage = string.IsNullOrWhiteSpace(character)
            ? "[Listing characters...]"
            : $"[Connecting as {character}...]";
        _dockManager.Route(Game.WindowTarget.Main, string.Empty,
            statusMessage + "\n", default, default);

        _ = Task.Run(() =>
        {
            try
            {
                _game.Connect(string.Empty, account, password, character, "DR");
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dockManager.Route(Game.WindowTarget.Main, string.Empty,
                        $"[Connect error: {ex.Message}]\n", default, default);
                    ConnectButton.IsEnabled = true;
                });
            }
        });
    }

    private void CommandBox_KeyDown(object? sender, KeyEventArgs e)
        => _controller.HandleKeyDown(e);

    private void OnPrintText(string text, Color color, Color bgcolor,
                             Game.WindowTarget targetwindow, string targetwindowstring,
                             bool mono, bool isprompt, bool isinput)
    {
        // mono: font switching deferred to font-configuration sub-project;
        //   both mono and non-mono runs are monospace in this pass (inherit control default).
        // isprompt, isinput: reserved for future sub-projects.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            _dockManager.Route(targetwindow, targetwindowstring, text, color, bgcolor));
    }

    private void OnDisconnected()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
        UpdateWindowTitle();
    }

    private void OnPrintError(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            _dockManager.Route(Game.WindowTarget.Main, string.Empty, text, default, default));
    }

    private void OnVariableChanged(string variable)
    {
        if (variable is "$gamename" or "$connected" or "$charactername")
            UpdateWindowTitle();
    }

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
    private void MenuLayout_Load(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: show OpenFileDialog, then call _dockManager.LoadLayout(path).
        // File-picker dialog deferred to a future task.
    }

    private void MenuLayout_LoadDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _dockManager.LoadDefaultLayout();

    private void MenuLayout_SaveAs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: show SaveFileDialog, then call _dockManager.SaveLayout(path).
        // File-picker dialog deferred to a future task.
    }

    private void MenuLayout_SaveDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _dockManager.SaveDefaultLayout();

    private void MenuLayout_SaveSizedDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* out of scope */ }
    private void MenuLayout_Basic(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* out of scope */ }
    private void MenuLayout_IconBarDockTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
    private void MenuLayout_IconBarDockBottom(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
    private void MenuLayout_ScriptBarDockTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
    private void MenuLayout_ScriptBarDockBottom(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
    private void MenuLayout_HealthBarDockTop(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
    private void MenuLayout_HealthBarDockBottom(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* TODO */ }
    private void MenuLayout_MagicPanels(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { /* out of scope */ }
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
}
