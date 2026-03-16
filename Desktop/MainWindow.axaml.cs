using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GenieClient;
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

    private Command? _command;
    private readonly ScriptList _scriptList    = new ScriptList();
    private readonly ScriptList _scriptListNew = new ScriptList();
    private DispatcherTimer? _gameLoopTimer;

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
        Closing += (_, _) =>
        {
            _gameLoopTimer?.Stop();
            _dockManager.SaveDefaultLayout();
        };

        Opened += OnWindowOpened;

        UpdateWindowTitle();
        _MenuPluginsNoPlugins.IsEnabled = false;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened; // fire once
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        AppendInit("Using Encoding: Unicode (UTF-8)\r\n");
        AppendInit($"Genie User Data Path: {LocalDirectory.Path}\r\n\r\n");

        // ConfigDir is read fresh inside each lambda so that if settings.cfg changes
        // the config path, all subsequent loads use the updated value.
        await RunLoad("Loading Settings...",         () => _game.Globals.Config.Load(_game.Globals.Config.ConfigDir + @"\settings.cfg"));
        await RunLoad("Loading Presets...",          () => _game.Globals.PresetList.Load(_game.Globals.Config.ConfigDir + @"\presets.cfg"));
        await RunLoad("Loading Global Variables...", () => _game.Globals.VariableList.Load(_game.Globals.Config.ConfigDir + @"\variables.cfg"));
        await RunLoad("Loading Highlights...",       () => _game.Globals.LoadHighlights(_game.Globals.Config.ConfigDir + @"\highlights.cfg"));
        await RunLoad("Loading Names...",            () => _game.Globals.NameList.Load(_game.Globals.Config.ConfigDir + @"\names.cfg"));
        await RunLoad("Loading Macros...",           () => _game.Globals.MacroList.Load(_game.Globals.Config.ConfigDir + @"\macros.cfg"));
        await RunLoad("Loading Aliases...",          () => _game.Globals.AliasList.Load(_game.Globals.Config.ConfigDir + @"\aliases.cfg"));
        await RunLoad("Loading Substitutes...",      () => _game.Globals.SubstituteList.Load(_game.Globals.Config.ConfigDir + @"\substitutes.cfg"));
        await RunLoad("Loading Gags...",             () => _game.Globals.GagList.Load(_game.Globals.Config.ConfigDir + @"\gags.cfg"));
        await RunLoad("Loading Triggers...",         () => _game.Globals.TriggerList.Load(_game.Globals.Config.ConfigDir + @"\triggers.cfg"));
        await RunLoad("Loading Classes...",          () => _game.Globals.ClassList.Load(_game.Globals.Config.ConfigDir + @"\classes.cfg"));

        WireGameEvents();
        WireCommandEvents();
        StartGameLoopTimer();
    }

    private async Task RunLoad(string label, Action load)
    {
        AppendInit(label);
        try
        {
            await Task.Run(load);
            AppendInit("OK\r\n");
        }
        catch
        {
            AppendInit("FAILED\r\n");
        }
    }

    private void AppendInit(string text) =>
        _dockManager.Route(Game.WindowTarget.Main, string.Empty,
                           text, Color.WhiteSmoke, Color.Empty);

    private void StartGameLoopTimer()
    {
        // Use explicit form (set Interval, wire Tick, then Start) to avoid ambiguity
        // about whether the 3-arg constructor auto-starts in Avalonia 11.
        _gameLoopTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        _gameLoopTimer.Tick += OnGameLoopTick;
        _gameLoopTimer.Start();
    }

    private void OnGameLoopTick(object? sender, EventArgs e)
    {
        // Poll event queue (custom Genie events, e.g. from scripts)
        var evtAction = _game.Globals.Events.Poll();
        if (!string.IsNullOrEmpty(evtAction))
            _ = _command?.ParseCommand(evtAction, false, false, "Event");

        // Poll command queue (script-queued commands with timing)
        var webbed  = _game.Globals.VariableList["webbed"]?.ToString()  == "1";
        var stunned = _game.Globals.VariableList["stunned"]?.ToString() == "1";
        string queueCmd = _game.Globals.CommandQueue.Poll(HasRoundTime(), webbed, stunned);
        while (!string.IsNullOrEmpty(queueCmd))
        {
            _ = _command?.ParseCommand(queueCmd, true, false, "Queue");
            queueCmd = _game.Globals.CommandQueue.Poll(HasRoundTime(), webbed, stunned);
        }

        // Tick all running scripts
        TickScripts();

        // Move newly-created scripts into the active list
        SafeAddScripts();

        // Remove scripts that have finished
        SafeRemoveExitedScripts();
    }

    private bool HasRoundTime() =>
        DateTime.Now < _game.Globals.RoundTimeEnd;  // mirrors FormMain HasRoundTime property

    private void TickScripts()
    {
        if (!_scriptList.AcquireReaderLock()) return;
        try
        {
            foreach (Script oScript in _scriptList)
                oScript.TickScript();   // method name is TickScript(), not Tick()
        }
        finally { _scriptList.ReleaseReaderLock(); }
    }

    // Mirrors FormMain.AddScripts() — lock order is _scriptList outer, _scriptListNew inner.
    private void SafeAddScripts()
    {
        if (_scriptListNew.Count == 0) return;
        if (!_scriptList.AcquireWriterLock()) return;
        try
        {
            if (_scriptListNew.AcquireWriterLock())
            {
                try
                {
                    foreach (Script s in _scriptListNew)
                        if (s != null) _scriptList.Add(s);
                    _scriptListNew.Clear();
                }
                finally { _scriptListNew.ReleaseWriterLock(); }
            }
            // else: unable to acquire inner lock — skip this tick
        }
        finally { _scriptList.ReleaseWriterLock(); }
    }

    // Note: there is a benign race between reader release and writer acquire where
    // indices could become stale if scripts are added concurrently. FormMain has
    // the same race — this is not a regression.
    private void SafeRemoveExitedScripts()
    {
        if (!_scriptList.AcquireReaderLock()) return;
        var removeList = new List<int>();
        try
        {
            for (int i = 0; i < _scriptList.Count; i++)
                if (_scriptList[i].ScriptDone) removeList.Add(i);
        }
        finally { _scriptList.ReleaseReaderLock(); }

        if (removeList.Count == 0) return;

        if (_scriptList.AcquireWriterLock())
        {
            try { for (int i = removeList.Count - 1; i >= 0; i--) _scriptList.RemoveAt(removeList[i]); }
            finally { _scriptList.ReleaseWriterLock(); }
        }
    }

    private void WireGameEvents()
    {
        // ── Active handlers ───────────────────────────────────────────────────────

        // Clear a named output panel (window name string, not WindowTarget enum)
        _game.EventClearWindow += (sWindow) =>
            Dispatcher.UIThread.Post(() => _dockManager.ClearPanel(sWindow));

        // Create/show a named sub-window on demand from game XML
        // Skip "main" — it always exists. Cast objects to string.
        _game.EventStreamWindow += (sID, sTitle, sIfClosed) =>
        {
            var name = sID?.ToString() ?? string.Empty;
            if (string.Equals(name, "main", StringComparison.OrdinalIgnoreCase)) return;
            // Register panel so it appears in the Windows menu, but don't auto-show it.
            // The user opens it manually; once opened, it stays in their layout.
            Dispatcher.UIThread.Post(() => _dockManager.RegisterPanel(name));
        };

        // Text line from server — run through triggers and notify scripts
        _game.EventTriggerParse += (sText) =>
            Task.Run(() => ParseTriggers(sText));

        // Server prompt received — notify all running scripts
        _game.EventTriggerPrompt += () =>
        {
            if (!_scriptList.AcquireReaderLock()) return;
            try { foreach (Script s in _scriptList) s.TriggerPrompt(); }
            finally { _scriptList.ReleaseReaderLock(); }
        };

        // Movement detected — notify all running scripts
        _game.EventTriggerMove += () =>
        {
            if (!_scriptList.AcquireReaderLock()) return;
            try { foreach (Script s in _scriptList) s.TriggerMove(); }
            finally { _scriptList.ReleaseReaderLock(); }
        };

        // ── Stubs (subscribed now, UI deferred to later sub-projects) ─────────────
        _game.EventDataRecieveEnd  += () => { /* TODO: end-of-update flush */ };
        _game.EventRoundTime       += (_) => { /* TODO: roundtime countdown bar */ };
        _game.EventCastTime        += ()  => { /* TODO: cast timer bar */ };
        _game.EventSpellTime       += ()  => { /* TODO: active spell timer */ };
        _game.EventClearSpellTime  += ()  => { /* TODO: clear spell timer display */ };
        _game.EventStatusBarUpdate += ()  => { /* TODO: vitals bars */ };
        _game.EventParseXML        += (_) => { /* TODO: plugin XML forwarding */ };
        _game.EventAddImage        += (filename, window, w, h) => { /* TODO: inline images */ };
    }

    private void ParseTriggers(string sText, bool bBufferWait = true)
    {
        if (!_triggersEnabled) return;
        if (string.IsNullOrWhiteSpace(sText)) return;

        // Trigger list — regex match → ParseCommand
        if (_game.Globals.TriggerList.AcquireReaderLock())
        {
            try
            {
                foreach (Globals.Triggers.Trigger oTrigger in _game.Globals.TriggerList.Values)
                {
                    try
                    {
                        if (!oTrigger.IsActive || oTrigger.bIsEvalTrigger) continue;
                        if (oTrigger.oRegexTrigger == null) continue;

                        var match = oTrigger.oRegexTrigger.Match(sText);
                        if (!match.Success) continue;

                        var args = new System.Collections.ArrayList();
                        for (int j = 1; j < match.Groups.Count; j++)
                            args.Add(match.Groups[j].Value);

                        // Substitute $1..$N into action string
                        var action = oTrigger.sAction;
                        for (int i = 0; i < _game.Globals.Config.iArgumentCount; i++)
                            action = action.Replace("$" + (i + 1),
                                i < args.Count ? args[i].ToString().Replace("\"", "") : string.Empty);
                        if (args.Count > 0)
                            action = action.Replace("$0", args[0].ToString().Replace("\"", ""));
                        else
                            action = action.Replace("$0", string.Empty);

                        _ = _command?.ParseCommand(action, true, false, "Trigger");
                    }
                    catch { /* ignore — continue processing remaining triggers */ }
                }
            }
            finally { _game.Globals.TriggerList.ReleaseReaderLock(); }
        }

        // Script list — notify each running script
        if (_scriptList.AcquireReaderLock())
        {
            try
            {
                foreach (Script s in _scriptList)
                {
                    try { s.TriggerParse(sText, bBufferWait); }
                    catch { /* ignore — continue processing remaining scripts */ }
                }
            }
            finally { _scriptList.ReleaseReaderLock(); }
        }
    }

    private void WireCommandEvents()
    {
        // Game.Globals is a read-only property; cannot pass by ref directly.
        var globals = _game.Globals;
        _command = new Command(ref globals);

        // ── Text output ──────────────────────────────────────────────────────────

        // Script/command echo to a named window (Color.WhiteSmoke, no BG)
        _command.EventEchoText += (sText, sWindow) =>
            Dispatcher.UIThread.Post(() => RouteCommandText(sText, sWindow, Color.WhiteSmoke, Color.Empty));

        // Script echo with explicit color
        _command.EventEchoColorText += (sText, oColor, oBgColor, sWindow) =>
            Dispatcher.UIThread.Post(() => RouteCommandText(sText, sWindow, oColor, oBgColor));

        // Link text — treat same as EchoText for now (link behavior deferred)
        _command.EventLinkText += (sText, sLink, sWindow) =>
            Dispatcher.UIThread.Post(() => RouteCommandText(sText, sWindow, Color.WhiteSmoke, Color.Empty));

        // ── Network ──────────────────────────────────────────────────────────────

        // Send raw bytes to game socket
        _command.EventSendRaw += (sText) =>
            _game.SendRaw(sText);

        // Send processed text to game (with optional trigger-on-input)
        _command.EventSendText += (sText, bUserInput, sOrigin) =>
        {
            _game.SendText(sText, bUserInput, sOrigin);
            if (_game.Globals.Config.bTriggerOnInput)
                Task.Run(() => ParseTriggers(sText));
        };

        // ── Parsing and scripts ──────────────────────────────────────────────────

        // Script-generated text line → run through triggers
        _command.EventParseLine += (sText) =>
        {
            if (!string.IsNullOrWhiteSpace(sText))
                Task.Run(() => ParseTriggers(sText, false));
        };

        // Run a named script file
        _command.EventRunScript += (sText) =>
            Task.Run(() => LoadAndRunScript(sText));

        // ── Window management ────────────────────────────────────────────────────

        // Command clears a named window (same handler as game EventClearWindow)
        _command.EventClearWindow += (sWindow) =>
            Dispatcher.UIThread.Post(() => _dockManager.ClearPanel(sWindow));

        // Command or script changes the window title
        _command.EventChangeWindowTitle += (sWindow, sComment) =>
            Dispatcher.UIThread.Post(() => UpdateWindowTitle());

        // ── Connection lifecycle ─────────────────────────────────────────────────

        _command.EventConnect    += (account, password, character, game, isLich) =>
            Task.Run(() => _game.Connect(string.Empty, account, password, character, game));
        _command.EventDisconnect += () => _game.Disconnect();
        _command.EventReconnect  += () => { /* TODO: reconnect — call Disconnect() then Connect() with saved profile; Game has no Reconnect() method */ };
        _command.EventExit       += () => Dispatcher.UIThread.Post(Close);

        // ── Variable changes (Command also fires this) ───────────────────────────
        _command.EventVariableChanged += OnVariableChanged;

        // ── Script UI (stubs — script panel UI deferred) ─────────────────────────
        _command.EventListScripts       += (_)    => { /* TODO: list scripts panel */ };
        _command.EventScriptTrace       += (_)    => { /* TODO: script trace panel */ };
        _command.EventScriptAbort       += (_)    => { /* TODO: script abort UI */ };
        _command.EventScriptPause       += (_)    => { /* TODO: script pause UI */ };
        _command.EventScriptPauseOrResume += (_)  => { /* TODO: script pause/resume UI */ };
        _command.EventScriptReload      += (_)    => { /* TODO: script reload UI */ };
        _command.EventScriptResume      += (_)    => { /* TODO: script resume UI */ };
        _command.EventScriptVariables   += (_, _) => { /* TODO: script variables panel */ };
        _command.EventPresetChanged     += (_)    => { /* TODO: reapply highlight colors */ };

        // ── Status bar / debug stubs ─────────────────────────────────────────────
        _command.EventStatusBar     += (_, _) => { /* TODO: status bar text */ };
        _command.EventScriptDebug   += (_, _) => { /* TODO: script debug output */ };

        // ── Plugin lifecycle (plugin phase) ──────────────────────────────────────
        _command.ListPlugins  += ()    => { /* TODO: plugin phase */ };
        _command.LoadPlugin   += (_)   => { /* TODO: plugin phase */ };
        _command.UnloadPlugin += (_)   => { /* TODO: plugin phase */ };
        _command.ReloadPlugins += ()   => { /* TODO: plugin phase */ };
        _command.DisablePlugin += (_)  => { /* TODO: plugin phase */ };
        _command.EnablePlugin  += (_)  => { /* TODO: plugin phase */ };

        // ── Image/misc stubs ─────────────────────────────────────────────────────
        _command.EventAddImage      += (f, w, wi, h) => { /* TODO: inline images */ };
    }

    private void RouteCommandText(string sText, string sWindow, Color oColor, Color oBgColor)
    {
        bool isMono = sText.StartsWith("mono ", StringComparison.OrdinalIgnoreCase);
        if (isMono) sText = sText[5..];

        if (string.IsNullOrEmpty(sWindow)
            || sWindow.Equals("game", StringComparison.OrdinalIgnoreCase)
            || sWindow.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            _dockManager.Route(Game.WindowTarget.Main, string.Empty, sText, oColor, oBgColor);
        }
        else
        {
            _dockManager.Route(Game.WindowTarget.Other, sWindow, sText, oColor, oBgColor);
        }
    }

    private void LoadAndRunScript(string sText)
    {
        var al = Utility.ParseArgs(sText, true);
        if (al.Count == 0) return;

        string scriptName = al[0].ToString()!.ToLower().Trim().TrimStart('#');
        if (!scriptName.EndsWith($".{_game.Globals.Config.ScriptExtension}"))
            scriptName += $"." + _game.Globals.Config.ScriptExtension;

        // Abort duplicate if configured
        if (_game.Globals.Config.bAbortDupeScript && _scriptList.AcquireReaderLock())
        {
            try
            {
                foreach (Script existing in _scriptList)
                    if (existing.FileName == scriptName) existing.AbortScript();
            }
            finally { _scriptList.ReleaseReaderLock(); }
        }

        var oScript = new Script(_game.Globals);
        oScript.EventPrintError  += (sErr)           => Dispatcher.UIThread.Post(() =>
            _dockManager.Route(Game.WindowTarget.Main, string.Empty, sErr, Color.WhiteSmoke, Color.DarkRed));
        oScript.EventPrintText   += (sTxt, clr, bg)  => Dispatcher.UIThread.Post(() =>
            _dockManager.Route(Game.WindowTarget.Main, string.Empty, sTxt, clr, bg));
        oScript.EventSendText    += (text, script, toQueue, doCommand) =>
            Task.Run(() => HandleScriptSendText(text, script, toQueue, doCommand));
        oScript.EventStatusChanged += (_, _) => { /* TODO: script status toolbar */ };
        oScript.EventDebugChanged  += (_, _) => { /* TODO: script debug UI */ };

        if (!oScript.LoadFile(scriptName, al)) return;

        if (_scriptListNew.AcquireWriterLock())
        {
            try { _scriptListNew.Add(oScript); }
            finally { _scriptListNew.ReleaseWriterLock(); }
        }
        oScript.RunScript();
    }

    private void HandleScriptSendText(string text, string script, bool toQueue, bool doCommand)
    {
        bool sendToGame = !text.StartsWith(_game.Globals.Config.cCommandChar.ToString());
        if (!toQueue)
        {
            _ = _command?.ParseCommand(text, sendToGame, false, script);
        }
        else
        {
            string sNumber = string.Empty;
            foreach (char c in text)
            {
                if (char.IsDigit(c) || c == '.') sNumber += c;
                else break;
            }
            double delay = sNumber.Length > 0 ? double.Parse(sNumber) : 0;
            string action = _game.Globals.ParseGlobalVars(
                sNumber.Length > 0 ? text[sNumber.Length..].Trim() : text);
            _game.Globals.CommandQueue.AddToQueue(delay, action, true, doCommand, doCommand);
        }
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
