// Desktop/Controls/VitalBarControl.axaml.cs
using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace GenieClient.Desktop.Controls;

public partial class VitalBarControl : UserControl
{
    private int    _value       = 100;
    private string _barText     = "";
    private IBrush _fillColor   = Brushes.DarkGray;
    private IBrush _emptyColor  = Brushes.Black;
    private bool   _isConnected = true;

    private ColumnDefinition? _fillColumn;
    private ColumnDefinition? _emptyColumn;
    private Border? _fillBorder;
    private Border? _emptyBorder;
    private TextBlock? _barLabel;
    private Grid? _barGrid;

    public VitalBarControl()
    {
        InitializeComponent();
        _barGrid     = this.FindControl<Grid>("BarGrid");
        _fillBorder  = this.FindControl<Border>("FillBorder");
        _emptyBorder = this.FindControl<Border>("EmptyBorder");
        _barLabel    = this.FindControl<TextBlock>("BarLabel");

        if (_barGrid?.ColumnDefinitions.Count >= 2)
        {
            _fillColumn  = _barGrid.ColumnDefinitions[0];
            _emptyColumn = _barGrid.ColumnDefinitions[1];
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            if (_fillColumn != null && _emptyColumn != null && _barLabel != null)
            {
                _fillColumn.Width  = new Avalonia.Controls.GridLength(_value,       Avalonia.Controls.GridUnitType.Star);
                _emptyColumn.Width = new Avalonia.Controls.GridLength(100 - _value, Avalonia.Controls.GridUnitType.Star);
                _barLabel.Text = _barText;
            }
        }
    }

    public string BarText
    {
        get => _barText;
        set { _barText = value; if (_barLabel != null) _barLabel.Text = value; }
    }

    public IBrush FillColor
    {
        get => _fillColor;
        set { _fillColor = value; ApplyBrushes(); }
    }

    public IBrush EmptyColor
    {
        get => _emptyColor;
        set { _emptyColor = value; ApplyBrushes(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; ApplyBrushes(); }
    }

    private void ApplyBrushes()
    {
        if (_fillBorder == null || _emptyBorder == null)
            return;

        if (_isConnected)
        {
            _fillBorder.Background  = _fillColor;
            _emptyBorder.Background = _emptyColor;
        }
        else
        {
            _fillBorder.Background  = Grayscale(_fillColor);
            _emptyBorder.Background = Grayscale(_emptyColor);
        }
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
