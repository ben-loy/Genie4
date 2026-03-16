using System;
using Avalonia.Controls;

namespace GenieClient.Desktop;

public partial class FloatingWindow : Window
{
    private readonly GameOutputPanel _panel;
    private readonly Action _dockAction;
    private readonly Action _hideAction;
    private bool _suppressHide;

    /// <param name="panel">The panel to host.</param>
    /// <param name="dockAction">Callback that runs DockManager.Dock(panel) when user clicks Dock.</param>
    /// <param name="hideAction">Callback that runs DockManager.OnPanelHiddenWhileFloating(panel) on close/hide.</param>
    public FloatingWindow(GameOutputPanel panel, Action dockAction, Action hideAction)
    {
        InitializeComponent();
        _panel = panel;
        _dockAction = dockAction;
        _hideAction = hideAction;

        Title = panel.WindowName;
        Content = panel;
        panel.IsFloating = true;

        panel.DockRequested  += OnDockRequested;
        panel.CloseRequested += OnCloseRequested;
        Closing += OnWindowClosing;
    }

    private void OnDockRequested(object? sender, EventArgs e)
    {
        // Unsubscribe from panel events before closing to prevent double-firing on reuse
        _panel.DockRequested  -= OnDockRequested;
        _panel.CloseRequested -= OnCloseRequested;

        // Set flag BEFORE calling dockAction so Closing handler skips hide logic.
        _suppressHide = true;
        _dockAction();   // DockManager.Dock(panel) — restores docked column
        Close();         // FloatingWindow closes itself after dock completes
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        // For docked panels: DockManager's CloseRequested subscription handles it directly.
        // For floating panels: this triggers the OS close path, which fires Closing below.
        Close();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Unsubscribe from panel events to prevent double-firing on reuse
        _panel.DockRequested  -= OnDockRequested;
        _panel.CloseRequested -= OnCloseRequested;

        if (_suppressHide) return;

        // User closed without docking.
        _panel.IsOutputHidden = true;
        _panel.IsFloating = false;
        _hideAction();  // DockManager.OnPanelHiddenWhileFloating(panel)
    }
}
