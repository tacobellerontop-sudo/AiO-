using System.Windows;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Updates;

namespace AiO.App.ViewModels;

/// <summary>One shared update state for the Settings page, the sidebar and the tray.</summary>
public sealed class UpdateViewModel : ObservableObject
{
    public static UpdateViewModel Instance { get; } = new();

    private AppRelease? _latest;
    private UpdateState _state = UpdateState.UpToDate;
    private bool _checked;
    private bool _busy;
    private double _progress;
    private string _status = "";

    /// <summary>Raised (on the UI thread) when a check finds a newer build.</summary>
    public event Action<AppRelease>? UpdateFound;

    private UpdateViewModel()
    {
        CheckCommand = new RelayCommand(() => _ = CheckAsync(userInitiated: true), () => !IsBusy);
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => !IsBusy && _latest is not null && _state != UpdateState.UpToDate);
        OpenReleasesCommand = new RelayCommand(() => SystemIntegration.OpenUrl(AppUpdater.ReleasesUrl));
        _status = CurrentBuildText;
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand OpenReleasesCommand { get; }

    public string CurrentBuildText => AppUpdater.CurrentCommit.Length > 0
        ? $"This build: {AppUpdater.CurrentCommit[..7]}"
        : "This build: local (commit unknown)";

    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) CommandManager.InvalidateRequerySuggested(); } }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>A newer build is published (also true when this build's commit is unknown).</summary>
    public bool IsAvailable => _checked && _latest is not null && _state != UpdateState.UpToDate;

    /// <summary>Known to be newer than this build: drives the sidebar badge.</summary>
    public bool IsNewer => _checked && _state == UpdateState.Available;

    public string InstallText => _state == UpdateState.Unknown ? "Install latest build" : "Update now";

    public async Task CheckAsync(bool userInitiated)
    {
        if (IsBusy) return;
        IsBusy = true;
        if (userInitiated) Status = "Checking for updates…";
        try
        {
            var latest = await AppUpdater.GetLatestAsync();
            _latest = latest;
            _state = AppUpdater.Compare(AppUpdater.CurrentCommit, latest);
            _checked = true;
            string when = latest.Published == DateTimeOffset.MinValue ? "" : $", built {latest.Published.ToLocalTime():d MMM yyyy HH:mm}";
            Status = _state switch
            {
                UpdateState.UpToDate => $"You're up to date ({latest.ShortCommit}{when}).",
                UpdateState.Available => $"A new build is available: {latest.ShortCommit}{when}.",
                _ => $"Latest build is {latest.ShortCommit}{when}. This copy can't tell if it's older.",
            };
            if (_state == UpdateState.Available) UpdateFound?.Invoke(latest);
        }
        catch (Exception ex)
        {
            if (userInitiated) Status = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(IsNewer));
            OnPropertyChanged(nameof(InstallText));
        }
    }

    private async Task InstallAsync()
    {
        if (_latest is not { } release || IsBusy) return;
        IsBusy = true;
        Progress = 0;
        try
        {
            Status = "Downloading the update…";
            var progress = new Progress<double>(p =>
            {
                Progress = p * 100;
                Status = $"Downloading the update… {p:P0}";
            });
            string files = await AppUpdater.DownloadAsync(release, progress);

            string installFolder = AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            bool elevate = !AppServices.IsAdministrator && !AppUpdater.CanWrite(installFolder);
            Status = "Installing — AiO will restart…";
            try
            {
                AppUpdater.StartInstaller(files, installFolder, SystemIntegration.RestartedArgument, elevate);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Status = "Update cancelled: AiO needs permission to write to its folder.";
                return;
            }
            ((App)Application.Current).ExitApp();
        }
        catch (Exception ex)
        {
            Status = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
