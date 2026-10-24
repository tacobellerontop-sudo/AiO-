using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AiO.App.Views;

public static class Dialogs
{
    public static bool Confirm(string message) =>
        MessageBox.Show(message, "AiO", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>
    /// "Keep these display settings?" with a 15-second countdown, like Windows Settings.
    /// Returns false (revert) on timeout so a blank screen fixes itself.
    /// </summary>
    public static bool ConfirmKeepDisplayChanges(int seconds = 15)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
        var keep = new Button { Content = "Keep changes", Style = (Style)Application.Current.FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var revert = new Button { Content = "Revert", IsCancel = true };

        var window = new Window
        {
            Title = "AiO",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("SurfaceBrush"),
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush"),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new TextBlock { Text = "Keep these display settings?", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                    text,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { keep, revert } },
                },
            },
        };

        int remaining = seconds;
        void UpdateText() => text.Text = $"Reverting to the previous settings in {remaining} seconds.";
        UpdateText();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (--remaining <= 0) { timer.Stop(); window.DialogResult = false; }
            else UpdateText();
        };
        keep.Click += (_, _) => window.DialogResult = true;
        timer.Start();
        bool result = window.ShowDialog() == true;
        timer.Stop();
        return result;
    }
}
