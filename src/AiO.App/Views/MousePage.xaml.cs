using System.Windows;
using System.Windows.Controls;
using AiO.App.ViewModels;

namespace AiO.App.Views;

public partial class MousePage : UserControl
{
    public MousePage() => InitializeComponent();

    /// <summary>Only read raw mouse input while the page is on screen.</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is not MouseViewModel vm) return;
        if (IsVisible) vm.StartLive();
        else vm.StopLive();
    }
}
