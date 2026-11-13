using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AiO.App.Views;

/// <summary>
/// A small popup near the bottom of the screen ("Turbo", "Microphone muted"...), like the ones laptop
/// vendors show for Fn keys. It never takes focus and clicks pass through it.
/// </summary>
public sealed class OsdWindow : Window
{
    private readonly TextBlock _glyph = new() { FontSize = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _text = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(1.8) };

    public OsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        _glyph.FontFamily = (FontFamily)Application.Current.FindResource("IconFont");
        _glyph.Foreground = (Brush)Application.Current.FindResource("AccentHoverBrush");
        _text.Foreground = (Brush)Application.Current.FindResource("TextBrush");

        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 12, 24, 12),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _glyph, _text } },
        };

        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => { if (!_hide.IsEnabled) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExTransparent | WsExToolWindow);
        };
    }

    public void Show(string glyph, string text)
    {
        _glyph.Text = glyph;
        _text.Text = text;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 80;

        _hide.Stop();
        _hide.Start();
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
