using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using GenieClient.Genie;
using Color = System.Drawing.Color;

namespace GenieClient.Desktop;

public partial class MainWindow : Window
{
    private readonly Game _game;
    private readonly CommandInputController _controller;

    public MainWindow(Game game)
    {
        InitializeComponent();
        _game = game;
        _game.EventPrintText += OnPrintText;
        _game.EventDisconnected += OnDisconnected;
        _game.EventPrintError += OnPrintError;
        _controller = new CommandInputController(_game, CommandBox, OutputScroll);
    }

    private void ConnectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var account   = AccountBox.Text   ?? string.Empty;
        var password  = PasswordBox.Text  ?? string.Empty;
        var character = CharacterBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            AppendOutput("[Connect] Account and password are required.\n");
            return;
        }

        // Disable before dispatching — ensures the button is disabled before OnDisconnected
        // could possibly fire (prevents a re-enable/disable race on immediate failure).
        ConnectButton.IsEnabled = false;
        AppendOutput((string.IsNullOrWhiteSpace(character)
            ? "[Listing characters...]"
            : $"[Connecting as {character}...]") + "\n");
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
                    AppendOutput($"[Connect error: {ex.Message}]\n");
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text, color, bgcolor));
    }

    private void OnDisconnected()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
    }

    private void OnPrintError(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
    }

    /// <summary>
    /// Converts a System.Drawing.Color to an Avalonia brush.
    /// Returns null for transparent or empty colours so the Run inherits the control default.
    /// </summary>
    private static IBrush? ToBrush(Color c)
        => (c.IsEmpty || c.A == 0)
           ? null
           : new SolidColorBrush(new Avalonia.Media.Color(c.A, c.R, c.G, c.B));

    private void AppendOutput(string text,
                               Color fg = default,
                               Color bg = default)
    {
        IBrush? fgBrush = ToBrush(fg);
        IBrush? bgBrush = ToBrush(bg);

        // Normalize line endings to guard against CRLF sequences from the game protocol.
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Each segment becomes a Run; LineBreaks are inserted between segments.
        // Capacity hint: worst case is one Run + one LineBreak per segment.
        string[] segments = text.Split('\n');
        var inlines = new List<Inline>(segments.Length * 2);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length > 0)
            {
                var run = new Run(segments[i]);
                if (fgBrush != null) run.Foreground = fgBrush;
                if (bgBrush != null) run.Background = bgBrush;
                inlines.Add(run);
            }
            if (i < segments.Length - 1)
                inlines.Add(new LineBreak());
        }

        OutputText.Inlines.AddRange(inlines);
        OutputScroll.Offset = new Avalonia.Vector(OutputScroll.Offset.X, double.MaxValue);
    }
}
