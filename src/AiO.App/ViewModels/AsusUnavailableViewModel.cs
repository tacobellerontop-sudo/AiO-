using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Asus;

namespace AiO.App.ViewModels;

/// <summary>Shown on the ASUS page when the G-Helper controls can't be loaded, explaining why.</summary>
public sealed class AsusUnavailableViewModel : ObservableObject
{
    private AsusDetection _detection = AppServices.Instance.AsusDetection;
    private bool _foundNow;

    public AsusUnavailableViewModel()
    {
        CheckAgainCommand = new RelayCommand(CheckAgain);
        RestartAppCommand = new RelayCommand(RestartApp);
        RestartAsAdminCommand = new RelayCommand(() =>
        {
            if (SystemIntegration.RestartAsAdministrator()) ((App)Application.Current).ExitApp();
        }, () => !AppServices.IsAdministrator);
        OpenSupportCommand = new RelayCommand(() => SystemIntegration.OpenUrl("https://www.asus.com/support/download-center/"));
    }

    public string Message => _foundNow
        ? "The ASUS driver is reachable now. Restart AiO to load the controls."
        : _detection.Message;

    public string Machine =>
        $"{(_detection.Manufacturer.Length > 0 ? _detection.Manufacturer : "Unknown maker")} · " +
        $"{(_detection.Model.Length > 0 ? _detection.Model : "unknown model")}";

    public bool IsAsus => _detection.IsAsus;
    public bool FoundNow => _foundNow;
    public bool CanElevate => !AppServices.IsAdministrator && _detection.IsAsus;

    public ICommand CheckAgainCommand { get; }
    public ICommand RestartAppCommand { get; }
    public ICommand RestartAsAdminCommand { get; }
    public ICommand OpenSupportCommand { get; }

    private void CheckAgain()
    {
        var (laptop, detection) = AsusLaptop.Detect();
        laptop?.Dispose();
        _detection = detection;
        _foundNow = detection.DriverOpened;
        foreach (var name in new[] { nameof(Message), nameof(Machine), nameof(IsAsus), nameof(FoundNow), nameof(CanElevate) })
            OnPropertyChanged(name);
    }

    private static void RestartApp()
    {
        // Same hand-off as the elevated restart: the new process waits for this one to exit.
        Process.Start(new ProcessStartInfo(SystemIntegration.ExecutablePath, SystemIntegration.RestartedArgument) { UseShellExecute = true });
        ((App)Application.Current).ExitApp();
    }
}
