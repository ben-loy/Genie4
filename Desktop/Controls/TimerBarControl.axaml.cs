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

    private ColumnDefinition _fillColumn  = null!;
    private ColumnDefinition _emptyColumn = null!;

    public TimerBarControl()
    {
        InitializeComponent();
        var grid = (Grid)Content!;
        _fillColumn  = grid.ColumnDefinitions[0];
        _emptyColumn = grid.ColumnDefinitions[1];
    }

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
            _fillColumn.Width  = new GridLength(0, GridUnitType.Star);
            _emptyColumn.Width = new GridLength(1, GridUnitType.Star);
            TimerLabel.Text    = "";
            FillBorder.Background  = Brushes.Transparent;
            EmptyBorder.Background = Brushes.Transparent;
        }
        else
        {
            _fillColumn.Width  = new GridLength(_remaining,          GridUnitType.Star);
            _emptyColumn.Width = new GridLength(Math.Max(0, _total - _remaining), GridUnitType.Star);
            TimerLabel.Text    = _remaining.ToString();
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
