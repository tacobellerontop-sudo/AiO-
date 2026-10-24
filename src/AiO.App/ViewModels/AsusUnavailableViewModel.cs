using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Asus;

namespace AiO.App.ViewModels;

/// <summary>Shown on the ASUS page when the G-Helper controls can't be loaded, explaining why.</summary>
public sealed class AsusUnavailableViewModel : ObservableObject
{
    private readonly AppServices _services = AppServices.Instance;

    public AsusUnavailableViewModel(Action reload)
    {
        CheckAgainCommand = new RelayCommand(reload);
        RestartAsAdminCommand = new RelayCommand(() =>
        {
            if (SystemIntegration.RestartAsAdministrator()) ((App)Application.Current).ExitApp();
        }, () => !AppServices.IsAdministrator);
        OpenSupportCommand = new RelayCommand(() => SystemIntegration.OpenUrl("https://www.asus.com/support/download-center/"));
    }

    private AsusDetection Detection => _services.AsusDetection;

    public string Message => Detection.DriverOpened && _services.AsusLoadError is { } error
        ? $"The ASUS driver is connected, but AiO hit an error while reading this laptop's settings:\n\n{error}\n\n" +
          "Please send this message so it can be fixed. \"Check again\" retries."
        : Detection.Message;

    public string Machine =>
        $"{(Detection.Manufacturer.Length > 0 ? Detection.Manufacturer : "Unknown maker")} · " +
        $"{(Detection.Model.Length > 0 ? Detection.Model : "unknown model")}";

    public bool IsAsus => Detection.IsAsus;
    public bool CanElevate => !AppServices.IsAdministrator && Detection.IsAsus;

    public ICommand CheckAgainCommand { get; }
    public ICommand RestartAsAdminCommand { get; }
    public ICommand OpenSupportCommand { get; }
}
