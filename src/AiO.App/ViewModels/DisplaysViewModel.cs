using System.Collections.ObjectModel;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.App.Views;
using AiO.Core.Display;

namespace AiO.App.ViewModels;

public sealed record Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

public sealed class DisplaysViewModel : ObservableObject
{
    public ObservableCollection<DisplayItemViewModel> Displays { get; } = [];
    public ICommand RefreshCommand { get; }

    public DisplaysViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public void Refresh()
    {
        Displays.Clear();
        try
        {
            foreach (var d in DisplayManager.GetDisplays().OrderByDescending(d => d.IsPrimary).ThenBy(d => d.DeviceName))
                // Deferred so a card can trigger a refresh from inside its own binding update.
                Displays.Add(new DisplayItemViewModel(d, () => System.Windows.Application.Current.Dispatcher.BeginInvoke(Refresh)));
        }
        catch (Exception ex)
        {
            AppServices.Instance.Report($"Could not read displays: {ex.Message}");
        }
    }
}

public sealed class DisplayItemViewModel : ObservableObject
{
    private readonly DisplayInfo _info;
    private readonly Action _refresh;
    private Resolution? _selectedResolution;
    private int _selectedRefreshRate;
    private DisplayOrientation _selectedOrientation;
    private int _selectedScale;

    public DisplayItemViewModel(DisplayInfo info, Action refresh)
    {
        _info = info;
        _refresh = refresh;
        Resolutions = info.Modes.Select(m => new Resolution(m.Width, m.Height)).Distinct().ToList();
        _selectedResolution = new Resolution(info.CurrentMode.Width, info.CurrentMode.Height);
        _selectedRefreshRate = info.CurrentMode.RefreshRate;
        _selectedOrientation = info.Orientation;
        _selectedScale = info.Scale?.CurrentPercent ?? 100;

        ApplyCommand = new RelayCommand(Apply);
        ApplyScaleCommand = new RelayCommand(ApplyScale, () => Scale is not null && SelectedScale != Scale.CurrentPercent);
    }

    public string Title => _info.FriendlyName;
    public string Subtitle =>
        $"{_info.ShortName} · {_info.AdapterName}{(_info.IsPrimary ? " · Primary" : "")} · now {_info.CurrentMode}";

    public IReadOnlyList<Resolution> Resolutions { get; }

    public Resolution? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (!Set(ref _selectedResolution, value)) return;
            OnPropertyChanged(nameof(RefreshRates));
            // Keep the refresh rate if the new resolution supports it, else pick its highest.
            if (!RefreshRates.Contains(SelectedRefreshRate)) SelectedRefreshRate = RefreshRates.FirstOrDefault();
        }
    }

    public IReadOnlyList<int> RefreshRates =>
        _info.Modes.Where(m => SelectedResolution is { } r && m.Width == r.Width && m.Height == r.Height)
            .Select(m => m.RefreshRate).Distinct().OrderByDescending(r => r).ToList();

    public int SelectedRefreshRate { get => _selectedRefreshRate; set => Set(ref _selectedRefreshRate, value); }

    public IReadOnlyList<DisplayOrientation> Orientations { get; } = Enum.GetValues<DisplayOrientation>();
    public DisplayOrientation SelectedOrientation { get => _selectedOrientation; set => Set(ref _selectedOrientation, value); }

    public DisplayScale? Scale => _info.Scale;
    public bool HasScale => Scale is not null;
    public IReadOnlyList<int> Scales => Scale?.AvailablePercents ?? [];
    public int SelectedScale { get => _selectedScale; set => Set(ref _selectedScale, value); }

    public bool HdrSupported => _info.Hdr?.Supported == true;

    public bool HdrEnabled
    {
        get => _info.Hdr?.Enabled == true;
        set
        {
            if (DisplayManager.SetHdr(_info, value))
                AppServices.Instance.Report($"HDR {(value ? "on" : "off")} for {Title}.");
            else
                AppServices.Instance.Report("Windows refused the HDR change.");
            _refresh();
        }
    }

    public ICommand ApplyCommand { get; }
    public ICommand ApplyScaleCommand { get; }

    private void Apply()
    {
        if (SelectedResolution is not { } res) return;
        var target = _info.Modes.FirstOrDefault(m => m.Width == res.Width && m.Height == res.Height && m.RefreshRate == SelectedRefreshRate);
        if (target is null) return;

        var previousMode = _info.CurrentMode;
        var previousOrientation = _info.Orientation;
        if (target == previousMode && SelectedOrientation == previousOrientation) return;

        var result = DisplayManager.SetMode(_info, target, SelectedOrientation);
        AppServices.Instance.Report(result.Message);
        if (result.Success && !result.RestartRequired && !Dialogs.ConfirmKeepDisplayChanges())
        {
            DisplayManager.SetMode(_info, previousMode, previousOrientation);
            AppServices.Instance.Report("Display settings reverted.");
        }
        _refresh();
    }

    private void ApplyScale()
    {
        AppServices.Instance.Report(DisplayManager.SetScale(_info, SelectedScale)
            ? $"Scale set to {SelectedScale}%. Some apps need restarting to pick it up."
            : "Windows refused the scale change.");
        _refresh();
    }
}
