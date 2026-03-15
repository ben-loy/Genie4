using System.Drawing;
using Avalonia.Controls;
using Avalonia.Input;
using GenieClient.Genie;

namespace GenieClient.Desktop;

public partial class MainWindow : Window
{
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
        // TODO(sub-project 2): Replace DirectConnect with Game.Connect(genieKey, account, password, character, "DR")
        // which routes through eaccess.play.net for full Simutronics account authentication.
        // Account and password fields are UI placeholders — collected here for future use.
        var account = AccountBox.Text ?? string.Empty;    // reserved for future EACCESS auth
        var password = PasswordBox.Text ?? string.Empty;  // reserved for future EACCESS auth
        var character = CharacterBox.Text ?? string.Empty;
        _ = account; _ = password; // suppress unused-variable warnings until wired

        if (string.IsNullOrWhiteSpace(character))
        {
            AppendOutput("[Connect] Character name is required.");
            return;
        }

        AppendOutput($"[Connecting as {character} to {DefaultHost}:{DefaultPort}...]");
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

    private void OnPrintText(string text, Color color, Color bgcolor, Game.WindowTarget targetwindow, string targetwindowstring, bool mono, bool isprompt, bool isinput)
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
