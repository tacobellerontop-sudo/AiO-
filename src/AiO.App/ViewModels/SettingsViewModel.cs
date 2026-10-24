using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Settings;

namespace AiO.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services = AppServices.Instance;
    private GeneralSettings General => _services.Settings.Current.General;

    public SettingsViewModel()
    {
        RestartAsAdminCommand = new RelayCommand(() =>
        {
            if (SystemIntegration.RestartAsAdministrator()) ((App)Application.Current).ExitApp();
        }, () => !IsAdministrator);
        OpenSettingsFolderCommand = new RelayCommand(() =>
            SystemIntegration.OpenFolder(System.IO.Path.GetDirectoryName(_services.Settings.FilePath)!));
        OpenUrlCommand = new RelayCommand(p => { if (p is string url) SystemIntegration.OpenUrl(url); });
    }

    public bool IsAdministrator => AppServices.IsAdministrator;
    public string AdminText => IsAdministrator
        ? "Running as administrator: every feature is available."
        : "Running without administrator rights. Unlocking the full gamma range needs admin, and so does an elevated start with Windows (no UAC prompt).";

    public bool StartWithWindows
    {
        get => General.StartWithWindows;
        set
        {
            try
            {
                SystemIntegration.SetStartWithWindows(value);
                General.StartWithWindows = value;
                Save();
            }
            catch (Exception ex)
            {
                _services.Report($"Could not change start-up registration: {ex.Message}");
            }
            OnPropertyChanged();
        }
    }

    public bool StartMinimized { get => General.StartMinimized; set { General.StartMinimized = value; Save(); } }
    public bool CloseToTray { get => General.CloseToTray; set { General.CloseToTray = value; Save(); } }
    public bool ApplyOnStartup { get => General.ApplyOnStartup; set { General.ApplyOnStartup = value; Save(); } }

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    public ICommand RestartAsAdminCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }
    public ICommand OpenUrlCommand { get; }

    private void Save([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _services.SaveSettings();
        OnPropertyChanged(name);
    }
}
