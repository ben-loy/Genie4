using System;
using System.Drawing;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using GenieClient.Genie;

namespace GenieClient.Desktop;

public partial class MainWindow : Window
{
    private readonly Game _game;

    public MainWindow(Game game)
    {
        InitializeComponent();
        _game = game;
        _game.EventPrintText += OnPrintText;
        _game.EventDisconnected += OnDisconnected;
        _game.EventPrintError += OnPrintError;
    }

    private void ConnectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var account   = AccountBox.Text   ?? string.Empty;
        var password  = PasswordBox.Text  ?? string.Empty;
        var character = CharacterBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            AppendOutput("[Connect] Account and password are required.");
            return;
        }

        // Disable before dispatching — ensures the button is disabled before OnDisconnected
        // could possibly fire (prevents a re-enable/disable race on immediate failure).
        ConnectButton.IsEnabled = false;
        AppendOutput(string.IsNullOrWhiteSpace(character)
            ? "[Listing characters...]"
            : $"[Connecting as {character}...]");
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
                    AppendOutput($"[Connect error: {ex.Message}]");
                    ConnectButton.IsEnabled = true;
                });
            }
        });
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

    private void OnPrintText(string text, Color color, Color bgcolor, Game.WindowTarget targetwindow, string targetwindowstring, bool mono, bool isprompt, bool isinput)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
    }

    private void OnDisconnected()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectButton.IsEnabled = true);
    }

    private void OnPrintError(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
    }

    private void AppendOutput(string text)
    {
        // TODO(sub-project 2): Replace with styled run appends (RichTextBlock or custom renderer).
        // String concatenation here is O(n) per line; acceptable only for foundation scaffolding.
        OutputText.Text = (OutputText.Text ?? string.Empty) + text + "\n";
        OutputScroll.Offset = new Avalonia.Vector(OutputScroll.Offset.X, double.MaxValue);
    }
}
