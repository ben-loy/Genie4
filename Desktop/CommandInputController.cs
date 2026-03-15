using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GenieClient.Genie;

namespace GenieClient.Desktop;

internal sealed class CommandInputController
{
    private readonly Game         _game;
    private readonly TextBox      _commandBox;
    private readonly ScrollViewer _outputScroll;

    // History (index 0 = most recent)
    private readonly List<string> _history = new();
    private int    _historyPos   = -1;           // -1 = "at new input"
    private string _pendingInput = string.Empty; // saved when nav begins

    // Tab completion
    private bool         _lastKeyWasTab;
    private string       _tabPattern = string.Empty;
    private List<string> _tabMatches = new();
    private int          _tabIndex;

    public CommandInputController(Game game, TextBox commandBox, ScrollViewer outputScroll)
    {
        _game         = game;
        _commandBox   = commandBox;
        _outputScroll = outputScroll;
    }

    public async void HandleKeyDown(KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        switch (e.Key)
        {
            case Key.Enter when ctrl:
                HandleCtrlEnter(e);
                break;

            case Key.Enter:
                HandleEnter(e);
                break;

            case Key.Up:
                ResetTabState();
                HandleUp(e);
                break;

            case Key.Down:
                ResetTabState();
                HandleDown(e);
                break;

            case Key.Tab:
                ResetHistoryNav();
                HandleTab(e);
                break;

            case Key.PageUp when ctrl:
                ResetTabState(); ResetHistoryNav();
                _outputScroll.Offset = new Vector(_outputScroll.Offset.X, 0);
                e.Handled = true;
                break;

            case Key.PageDown when ctrl:
                ResetTabState(); ResetHistoryNav();
                _outputScroll.Offset = new Vector(_outputScroll.Offset.X, double.MaxValue);
                e.Handled = true;
                break;

            case Key.PageUp:
                ResetTabState(); ResetHistoryNav();
                _outputScroll.Offset = new Vector(
                    _outputScroll.Offset.X,
                    Math.Max(0, _outputScroll.Offset.Y - _outputScroll.Viewport.Height));
                e.Handled = true;
                break;

            case Key.PageDown:
                ResetTabState(); ResetHistoryNav();
                _outputScroll.Offset = new Vector(
                    _outputScroll.Offset.X,
                    _outputScroll.Offset.Y + _outputScroll.Viewport.Height);
                e.Handled = true;
                break;

            case Key.V when ctrl:
                ResetTabState(); ResetHistoryNav();
                await HandlePasteAsync(e);
                break;

            default:
                ResetTabState();
                ResetHistoryNav();
                break;
        }
    }

    // ── Send ─────────────────────────────────────────────────────────────────

    private void HandleEnter(KeyEventArgs e)
    {
        string text = _commandBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) { e.Handled = true; return; }
        ExecuteSend(text);
        e.Handled = true;
    }

    private void HandleCtrlEnter(KeyEventArgs e)
    {
        if (_history.Count == 0) { e.Handled = true; return; }
        string text = _history[0];
        _commandBox.Text = text;
        ExecuteSend(text);
        e.Handled = true;
    }

    private void ExecuteSend(string text)
    {
        _game.SendText(text, bUserInput: true);
        AddToHistory(text);
        ResetHistoryNav();
        ResetTabState();
        if (_game.Globals.Config.bKeepInput)
            _commandBox.SelectAll();
        else
            _commandBox.Clear();
    }

    // ── History ───────────────────────────────────────────────────────────────

    private void AddToHistory(string text)
    {
        if (text.Length < 3) return;
        if (_history.Count > 0 && _history[0] == text) return;
        _history.Insert(0, text);
        if (_history.Count > 20)
            _history.RemoveAt(_history.Count - 1);
    }

    private void HandleUp(KeyEventArgs e)
    {
        if (_history.Count == 0) { e.Handled = true; return; }
        if (_historyPos == -1)
            _pendingInput = _commandBox.Text ?? string.Empty;
        _historyPos = Math.Min(_historyPos + 1, _history.Count - 1);
        _commandBox.Text = _history[_historyPos];
        _commandBox.CaretIndex = _commandBox.Text.Length;
        e.Handled = true;
    }

    private void HandleDown(KeyEventArgs e)
    {
        if (_historyPos == -1) { e.Handled = true; return; }
        _historyPos--;
        _commandBox.Text = _historyPos == -1 ? _pendingInput : _history[_historyPos];
        _commandBox.CaretIndex = (_commandBox.Text ?? string.Empty).Length;
        e.Handled = true;
    }

    private void ResetHistoryNav()
    {
        _historyPos   = -1;
        _pendingInput = string.Empty;
    }

    // ── Tab Completion ────────────────────────────────────────────────────────

    private void HandleTab(KeyEventArgs e)
    {
        string text = _commandBox.Text ?? string.Empty;

        if (!_lastKeyWasTab)
        {
            _tabPattern = text;
            _tabMatches = BuildTabMatches(text);
            _tabIndex   = 0;
        }

        e.Handled = true;

        if (_tabMatches.Count == 0) return;

        if (_tabMatches.Count == 1)
        {
            _commandBox.Text = _tabMatches[0] + " ";
            _commandBox.CaretIndex = _commandBox.Text.Length;
            _lastKeyWasTab = false;
            return;
        }

        _commandBox.Text = _tabMatches[_tabIndex];
        _commandBox.CaretIndex = _commandBox.Text.Length;
        _tabIndex      = (_tabIndex + 1) % _tabMatches.Count;
        _lastKeyWasTab = true;
    }

    private List<string> BuildTabMatches(string text)
    {
        // Guard 1: script context (starts with ScriptChar, no trailing space)
        if (text.Length > 0
            && text[0] == _game.Globals.Config.ScriptChar
            && !text.EndsWith(' '))
        {
            string prefix    = text[1..];
            string scriptDir = _game.Globals.Config.ScriptDir;
            if (!Directory.Exists(scriptDir)) return new List<string>();
            return Directory.GetFiles(scriptDir)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n != null && n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Guard 2: trailing space — nothing to complete
        if (text.EndsWith(' ')) return new List<string>();

        // Guard 3: alias context
        var matches = new List<string>();
        foreach (object key in _game.Globals.AliasList.Keys)
        {
            string k = (string)key;
            if (k.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                matches.Add(k);
        }
        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    private void ResetTabState()
    {
        _lastKeyWasTab = false;
        _tabPattern    = string.Empty;
        _tabMatches.Clear();
        _tabIndex = 0;
    }

    // ── Smart Paste ───────────────────────────────────────────────────────────

    private async Task HandlePasteAsync(KeyEventArgs e)
    {
        e.Handled = true;

        var topLevel = TopLevel.GetTopLevel(_commandBox);
        if (topLevel?.Clipboard == null) return;

        string? raw = await topLevel.Clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(raw)) return;

        if (raw.Length > 100)
        {
            bool confirmed = await ConfirmPasteAsync(raw.Length, topLevel as Window);
            if (!confirmed) return;
        }

        // Strip CR/LF; trim trailing whitespace
        string cleaned = raw.Replace("\r", "").Replace("\n", "").TrimEnd();

        int    start   = _commandBox.SelectionStart;
        int    end     = _commandBox.SelectionEnd;
        string current = _commandBox.Text ?? string.Empty;

        // SelectedText is read-only in Avalonia 11; splice via Text assignment
        _commandBox.Text       = current[..start] + cleaned + current[end..];
        _commandBox.CaretIndex = start + cleaned.Length;
    }

    private static async Task<bool> ConfirmPasteAsync(int charCount, Window? owner)
    {
        // If no owner window is available, allow paste silently
        if (owner == null) return true;

        var yesBtn = new Button { Content = "Yes" };
        var noBtn  = new Button { Content = "No"  };

        var dialog = new Window
        {
            Title                 = "Confirm Paste",
            Width                 = 340,
            Height                = 110,
            CanResize             = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin   = new Thickness(16),
                Spacing  = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text         = $"Paste {charCount} characters?",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation         = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing             = 8,
                        Children            = { yesBtn, noBtn }
                    }
                }
            }
        };

        // Close(result) sets the ShowDialog<T> return value
        yesBtn.Click += (_, _) => dialog.Close(true);
        noBtn.Click  += (_, _) => dialog.Close(false);

        return await dialog.ShowDialog<bool>(owner);
    }
}
