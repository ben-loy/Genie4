using System;
using System.Collections.Generic;
using System.Drawing;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Color = System.Drawing.Color;

namespace GenieClient.Desktop;

public partial class GameOutputPanel : UserControl
{
    public string WindowName { get; }

    private bool _isFloating;

    public bool IsFloating
    {
        get => _isFloating;
        set
        {
            _isFloating = value;
            // Float button shown when NOT floating; Dock button shown when floating.
            FloatButton.IsVisible = !value;
            DockButton.IsVisible = value;
        }
    }

    public bool IsOutputHidden { get; set; }

    // Expose inner ScrollViewer so DockManager can hand it to CommandInputController.
    public ScrollViewer OutputScroll => OutputScrollViewer;

    public event EventHandler? FloatRequested;
    public event EventHandler? DockRequested;
    public event EventHandler? CloseRequested;

    public GameOutputPanel(string windowName)
    {
        InitializeComponent();
        WindowName = windowName;
        TitleLabel.Text = windowName;

        // Initial state: docked (Float visible, Dock hidden).
        FloatButton.IsVisible = true;
        DockButton.IsVisible = false;

        FloatButton.Click += (_, _) => FloatRequested?.Invoke(this, EventArgs.Empty);
        DockButton.Click  += (_, _) => DockRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void AppendText(string text, Color fg, Color bg)
    {
        if (IsOutputHidden) return;

        IBrush? fgBrush = ToBrush(fg);
        IBrush? bgBrush = ToBrush(bg);

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
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

        OutputText.Inlines!.AddRange(inlines);
        OutputScrollViewer.Offset = new Avalonia.Vector(
            OutputScrollViewer.Offset.X, double.MaxValue);
    }

    private static IBrush? ToBrush(Color c)
        => (c.IsEmpty || c.A == 0)
           ? null
           : new SolidColorBrush(new Avalonia.Media.Color(c.A, c.R, c.G, c.B));
}
