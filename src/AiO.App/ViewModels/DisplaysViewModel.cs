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
    private readonly Debouncer _brightnessDebounce = new(TimeSpan.FromMilliseconds(60));
    private int? _panelBrightness;

    public ObservableCollection<DisplayItemViewModel> Displays { get; } = [];
    public ICommand RefreshCommand { get; }
    /// <summary>Win+P: parameter is a <see cref="ProjectionMode"/> name.</summary>
    public ICommand ProjectCommand { get; }

    public DisplaysViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        ProjectCommand = new RelayCommand(p =>
        {
            if (p is not string name || !Enum.TryParse<ProjectionMode>(name, out var mode)) return;
            AppServices.Instance.Report(DisplayExtras.SetProjection(mode)
                ? "Projection changed. If the picture is wrong, press Win+P to pick another option."
                : "Windows refused that projection mode. Is a second screen connected?");
            // Windows takes a moment to settle the new topology.
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); Refresh(); };
            timer.Start();
        });
        Refresh();
    }

    /// <summary>The built-in screen's brightness exposed through WMI (most laptops).</summary>
    public bool HasPanelBrightness => _panelBrightness is not null;

    public double PanelBrightness
    {
        get => _panelBrightness ?? 0;
        set
        {
            int v = (int)Math.Round(value);
            if (_panelBrightness == v) return;
            _panelBrightness = v;
            OnPropertyChanged();
            _brightnessDebounce.Run(() => Task.Run(() => DisplayExtras.SetPanelBrightness(v)));
        }
    }

    public void Refresh()
    {
        Displays.Clear();
        try
        {
            var all = DisplayManager.GetDisplays();
            foreach (var d in all.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.DeviceName))
                // Deferred so a card can trigger a refresh from inside its own binding update.
                Displays.Add(new DisplayItemViewModel(d, all, () => System.Windows.Application.Current.Dispatcher.BeginInvoke(Refresh)));
        }
        catch (Exception ex)
        {
            AppServices.Instance.Report($"Could not read displays: {ex.Message}");
        }
        _panelBrightness = DisplayExtras.GetPanelBrightness();
        OnPropertyChanged(nameof(PanelBrightness));
        OnPropertyChanged(nameof(HasPanelBrightness));
    }
}

public sealed class DisplayItemViewModel : ObservableObject
{
    private readonly DisplayInfo _info;
    private readonly IReadOnlyList<DisplayInfo> _all;
    private readonly Action _refresh;
    private Resolution? _selectedResolution;
    private int _selectedRefreshRate;
    private DisplayOrientation _selectedOrientation;
    private int _selectedScale;

    public DisplayItemViewModel(DisplayInfo info, IReadOnlyList<DisplayInfo> all, Action refresh)
    {
        _info = info;
        _all = all;
        _refresh = refresh;
        Resolutions = info.Modes.Select(m => new Resolution(m.Width, m.Height)).Distinct().ToList();
        _selectedResolution = new Resolution(info.CurrentMode.Width, info.CurrentMode.Height);
        _selectedRefreshRate = info.CurrentMode.RefreshRate;
        _selectedOrientation = info.Orientation;
        _selectedScale = info.Scale?.CurrentPercent ?? 100;

        ApplyCommand = new RelayCommand(Apply);
        ApplyScaleCommand = new RelayCommand(ApplyScale, () => Scale is not null && SelectedScale != Scale.CurrentPercent);
        MakePrimaryCommand = new RelayCommand(MakePrimary);
        RefreshPresetCommand = new RelayCommand(p =>
        {
            if (p is not string which) return;
            var rates = RefreshRates;
            if (rates.Count == 0) return;
            SelectedRefreshRate = which == "max" ? rates.Max() : rates.Min();
            Apply();
        });
    }

    public bool IsPrimary => _info.IsPrimary;
    public bool CanMakePrimary => !_info.IsPrimary && _all.Count > 1;
    public ICommand MakePrimaryCommand { get; }
    /// <summary>"max" or "min": jump to the highest or lowest refresh rate at the current resolution.</summary>
    public ICommand RefreshPresetCommand { get; }

    private void MakePrimary()
    {
        AppServices.Instance.Report(DisplayExtras.SetPrimary(_info, _all)
            ? $"{Title} is now the main display."
            : "Windows refused to change the main display.");
        _refresh();
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
