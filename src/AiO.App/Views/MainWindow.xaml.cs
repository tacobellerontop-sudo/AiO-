using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiO.App.ViewModels;

namespace AiO.App.Views;

/// <summary>
/// Borderless window with a custom title bar. The maximise button reports itself as HTMAXBUTTON so
/// Windows 11 still shows Snap Layouts when hovering it.
/// </summary>
public partial class MainWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP = 0x00A2;
    private const int WM_NCMOUSELEAVE = 0x02A2;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private const int HTMAXBUTTON = 9;

    private bool _maxHot;

    public MainWindow()
    {
        InitializeComponent();
        using var icon = Infrastructure.TrayIcon.CreateIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

        StateChanged += (_, _) => UpdateMaximizeGlyph();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnViewModelChanged;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Windows 11: rounded corners and a dark frame. Ignored on older versions.
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage)) AnimatePage();
    }

    private void AnimatePage()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        if (PageHost.RenderTransform is TranslateTransform t)
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void UpdateMaximizeGlyph()
    {
        bool max = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = max ? "" : "";
        MaximizeButton.ToolTip = max ? "Restore" : "Maximise";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private bool IsOverMaximizeButton(IntPtr lParam)
    {
        int x = unchecked((short)(long)lParam), y = unchecked((short)((long)lParam >> 16));
        try
        {
            var point = MaximizeButton.PointFromScreen(new Point(x, y));
            return point.X >= 0 && point.Y >= 0 && point.X < MaximizeButton.ActualWidth && point.Y < MaximizeButton.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // not yet in the visual tree
        }
    }

    private void SetMaxHot(bool hot)
    {
        if (_maxHot == hot) return;
        _maxHot = hot;
        MaximizeButton.Background = hot ? (Brush)FindResource("SurfaceHoverBrush") : Brushes.Transparent;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (IsOverMaximizeButton(lParam))
                {
                    SetMaxHot(true);
                    handled = true;
                    return HTMAXBUTTON;
                }
                SetMaxHot(false);
                break;
            case WM_NCLBUTTONDOWN when wParam == HTMAXBUTTON:
                handled = true;
                break;
            case WM_NCLBUTTONUP when wParam == HTMAXBUTTON:
                handled = true;
                ToggleMaximize();
                break;
            case WM_NCMOUSELEAVE:
            case WM_NCMOUSEMOVE when wParam != HTMAXBUTTON:
                SetMaxHot(false);
                break;
        }
        return IntPtr.Zero;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
