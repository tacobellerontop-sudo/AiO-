using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.App.Views;
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
        ExportSettingsCommand = new RelayCommand(ExportSettings);
        ImportSettingsCommand = new RelayCommand(ImportSettings);
        ResetSettingsCommand = new RelayCommand(ResetSettings);
        RestartAppCommand = new RelayCommand(() => Restart());
    }

    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }
    public ICommand ResetSettingsCommand { get; }
    public ICommand RestartAppCommand { get; }

    public bool ShowOsd { get => General.ShowOsd; set { General.ShowOsd = value; Save(); } }

    private string SettingsPath => _services.Settings.FilePath;

    private void ExportSettings()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"AiO settings {DateTime.Now:yyyy-MM-dd}.json",
            Filter = "AiO settings (*.json)|*.json",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _services.Settings.Save();
            System.IO.File.Copy(SettingsPath, dialog.FileName, overwrite: true);
            _services.Report("Settings backed up.");
        }
        catch (Exception ex) { _services.Report($"Could not save the backup: {ex.Message}"); }
    }

    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "AiO settings (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(dialog.FileName)) as System.Text.Json.Nodes.JsonObject;
            if (root?["General"] is null)
            {
                _services.Report("That file isn't an AiO settings backup.");
                return;
            }
            // A backup from another PC (or another day) must never undervolt at start-up by itself.
            if (root["Asus"]?["Ryzen"] is System.Text.Json.Nodes.JsonObject ryzen) ryzen["ApplyAutomatically"] = false;

            if (!Dialogs.Confirm("Replace all AiO settings with this backup? AiO restarts to load it.")) return;
            System.IO.File.Copy(SettingsPath, SettingsPath + ".before-import", overwrite: true);
            System.IO.File.WriteAllText(SettingsPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Restart(saveFirst: false);
        }
        catch (Exception ex) { _services.Report($"Could not read that file: {ex.Message}"); }
    }

    private void ResetSettings()
    {
        if (!Dialogs.Confirm("Reset every AiO setting to its default? Your current settings are kept as a backup next to the settings file, and AiO restarts.")) return;
        try
        {
            if (System.IO.File.Exists(SettingsPath)) System.IO.File.Move(SettingsPath, SettingsPath + ".before-reset", overwrite: true);
            Restart(saveFirst: false);
        }
        catch (Exception ex) { _services.Report($"Could not reset: {ex.Message}"); }
    }

    private void Restart(bool saveFirst = true)
    {
        if (saveFirst) _services.SaveSettings();
        if (SystemIntegration.RestartApp()) ((App)Application.Current).ExitApp(saveSettings: saveFirst);
        else _services.Report("Could not restart AiO.");
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
    public bool CheckForUpdates { get => General.CheckForUpdates; set { General.CheckForUpdates = value; Save(); } }

    public UpdateViewModel Updates => UpdateViewModel.Instance;

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
