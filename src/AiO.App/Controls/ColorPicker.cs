using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AiO.Core.Razer;

namespace AiO.App.Controls;

/// <summary>A colour swatch that opens a palette with presets, RGB sliders and a hex box.</summary>
public sealed class ColorPicker : ContentControl
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(string), typeof(ColorPicker),
            new FrameworkPropertyMetadata("#FF0000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorPicker)d).Sync()));

    /// <summary>Hex colour, "#RRGGBB".</summary>
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    private static readonly string[] Presets =
    [
        "#FFFFFF", "#FF0000", "#FF6A00", "#FFD800", "#B6FF00", "#00FF00", "#00FFB4", "#00D8FF",
        "#0080FF", "#0000FF", "#8000FF", "#FF00DC", "#FF0080", "#FF8080", "#80FFC0", "#000000",
    ];

    private readonly Border _swatch;
    private readonly Popup _popup;
    private readonly TextBox _hex;
    private readonly Slider[] _rgb = new Slider[3];
    private bool _syncing;

    public ColorPicker()
    {
        var border = (Brush)Application.Current.FindResource("BorderBrush");
        _swatch = new Border
        {
            Width = 44,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        _swatch.MouseLeftButtonUp += (_, _) => _popup!.IsOpen = !_popup.IsOpen;

        var panel = new StackPanel { Width = 236 };
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var preset in Presets)
        {
            var chip = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = Brush(preset),
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            chip.MouseLeftButtonUp += (_, _) => Color = preset;
            grid.Children.Add(chip);
        }
        panel.Children.Add(grid);

        string[] names = ["R", "G", "B"];
        for (int i = 0; i < 3; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock { Text = names[i], Width = 16, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            var slider = new Slider { Minimum = 0, Maximum = 255, IsSnapToTickEnabled = true, TickFrequency = 1 };
            slider.ValueChanged += (_, _) => FromSliders();
            _rgb[i] = slider;
            row.Children.Add(slider);
            panel.Children.Add(row);
        }

        _hex = new TextBox { Margin = new Thickness(0, 8, 0, 0) };
        _hex.LostFocus += (_, _) => FromHex();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromHex(); };
        panel.Children.Add(_hex);

        _popup = new Popup
        {
            PlacementTarget = _swatch,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 0),
                Child = panel,
            },
        };

        Content = new Grid { Children = { _swatch, _popup } };
        Sync();
    }

    private static SolidColorBrush Brush(string hex)
    {
        var c = RgbColor.FromHex(hex);
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
    }

    private void Sync()
    {
        if (_swatch is null || _syncing) return;
        RgbColor c;
        try { c = RgbColor.FromHex(Color); }
        catch (FormatException) { return; }

        _syncing = true;
        _swatch.Background = Brush(Color);
        _hex.Text = c.ToHex();
        _rgb[0].Value = c.R;
        _rgb[1].Value = c.G;
        _rgb[2].Value = c.B;
        _syncing = false;
    }

    private void FromSliders()
    {
        if (_syncing) return;
        Color = new RgbColor((byte)_rgb[0].Value, (byte)_rgb[1].Value, (byte)_rgb[2].Value).ToHex();
    }

    private void FromHex()
    {
        try { Color = RgbColor.FromHex(_hex.Text).ToHex(); }
        catch (FormatException) { _hex.Text = Color; }
    }
}
