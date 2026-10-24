using System.Collections.ObjectModel;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Color;
using AiO.Core.Display;
using AiO.Core.Gpu;
using AiO.Core.Settings;

namespace AiO.App.ViewModels;

public sealed class ColorViewModel : ObservableObject
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly Debouncer _gammaDebounce = new(TimeSpan.FromMilliseconds(40));
    private readonly SemaphoreSlim _ddcGate = new(1, 1);
    private IReadOnlyList<DdcMonitor> _ddcMonitors = [];
    private DisplayInfo? _selected;
    private ColorAdjustment _gamma = ColorAdjustment.Neutral;
    private bool _loading;
    private bool _ddcLoading;
    private uint? _selectedInput;

    public IReadOnlyList<DisplayInfo> Displays { get; private set; } = [];

    public DisplayInfo? SelectedDisplay
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) LoadDisplay();
        }
    }

    public ObservableCollection<GpuPropertyViewModel> GpuProperties { get; } = [];
    public ObservableCollection<DdcControlViewModel> DdcControls { get; } = [];
    public bool HasGpuControls => GpuProperties.Count > 0;
    public string GpuVendorText =>
        _services.GpuColor.Count == 0 ? "No NVIDIA or AMD driver detected." : string.Join(" + ", _services.GpuColor.Select(g => g.Vendor));

    public bool HasDdc => DdcControls.Count > 0;
    public bool ShowNoDdc => !HasDdc && !DdcLoading;

    public bool DdcLoading
    {
        get => _ddcLoading;
        private set
        {
            if (Set(ref _ddcLoading, value)) OnPropertyChanged(nameof(ShowNoDdc));
        }
    }
    public IReadOnlyDictionary<uint, string> InputSources => DdcMonitor.InputSources;
    public uint? SelectedInput { get => _selectedInput; set => Set(ref _selectedInput, value); }

    public bool FullRangeUnlocked => GammaController.IsFullRangeUnlocked();

    public ICommand ResetGammaCommand { get; }
    public ICommand ResetGpuCommand { get; }
    public ICommand UnlockRangeCommand { get; }
    public ICommand ApplyInputCommand { get; }
    public ICommand ReloadCommand { get; }

    // ---- Presets -----------------------------------------------------------------

    private string _newPresetName = "";
    private bool _includeMonitor = true;

    public ObservableCollection<PresetItemViewModel> Presets { get; } = [];
    public string NewPresetName { get => _newPresetName; set => Set(ref _newPresetName, value); }
    /// <summary>Also store the monitor's own brightness/contrast (DDC/CI) in the saved preset.</summary>
    public bool IncludeMonitorInPreset { get => _includeMonitor; set => Set(ref _includeMonitor, value); }
    public string ActivePresetText => SavedForSelected?.Preset is { } name ? $"Active: {name}" : "Custom settings";

    public ICommand ApplyPresetCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand ApplyPresetToAllCommand { get; }

    private void RefreshPresets()
    {
        string? active = SavedForSelected?.Preset;
        Presets.Clear();
        foreach (var preset in ColorPresets.All)
            Presets.Add(new PresetItemViewModel(preset, string.Equals(preset.Name, active, StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(ActivePresetText));
    }

    private DdcMonitor? SelectedMonitor =>
        _ddcMonitors.FirstOrDefault(m => m.DeviceName.Equals(_selected?.DeviceName, StringComparison.OrdinalIgnoreCase));

    private async Task ApplyPresetAsync(object? parameter)
    {
        if (parameter is not PresetItemViewModel item || _selected is null) return;
        string device = _selected.DeviceName;
        var monitor = SelectedMonitor;
        await _ddcGate.WaitAsync();
        try
        {
            await Task.Run(() => ColorPresets.Apply(device, item.Preset, monitor));
        }
        finally
        {
            _ddcGate.Release();
        }
        LoadDisplay();
        _services.Report($"\"{item.Name}\" applied to {_selected.FriendlyName}.");
    }

    private async Task ApplyPresetToAllAsync(object? parameter)
    {
        if (parameter is not PresetItemViewModel item) return;
        await ColorPresets.ApplyToAllAsync(item.Preset);
        LoadDisplay();
    }

    private void SavePreset()
    {
        string name = NewPresetName.Trim();
        if (name.Length == 0) { _services.Report("Type a name for the preset first."); return; }

        var saturation = GpuProperties.FirstOrDefault(p => p.Property == GpuColorProperty.Saturation);
        double? Fraction(byte code) =>
            IncludeMonitorInPreset && DdcControls.FirstOrDefault(c => c.Code == code) is { Maximum: > 0 } c ? c.Value / c.Maximum : null;

        var preset = new ColorPreset
        {
            Name = name,
            Gamma = _gamma,
            Saturation = saturation is null ? null
                : ColorPresetMath.ToRelative(new GpuColorRange(saturation.Value, saturation.Default, saturation.Min, saturation.Max), saturation.Value),
            MonitorBrightness = Fraction(VcpCode.Brightness),
            MonitorContrast = Fraction(VcpCode.Contrast),
        };
        if (!ColorPresets.SaveUserPreset(preset))
        {
            _services.Report($"\"{name}\" is a built-in preset name; pick another.");
            return;
        }
        if (SavedForSelected is { } saved) saved.Preset = name;
        _services.SaveSettings();
        NewPresetName = "";
        RefreshPresets();
        _services.Report($"Saved preset \"{name}\".");
    }

    private void DeletePreset(object? parameter)
    {
        if (parameter is not PresetItemViewModel { IsBuiltIn: false } item) return;
        ColorPresets.DeleteUserPreset(item.Name);
        RefreshPresets();
    }

    /// <summary>Any manual change means the display no longer matches a preset.</summary>
    private void MarkCustom()
    {
        if (SavedForSelected is { Preset: not null } saved)
        {
            saved.Preset = null;
            RefreshPresets();
        }
    }

    public ColorViewModel()
    {
        ApplyPresetCommand = new AsyncCommand(ApplyPresetAsync);
        ApplyPresetToAllCommand = new AsyncCommand(ApplyPresetToAllAsync);
        SavePresetCommand = new RelayCommand(SavePreset);
        DeletePresetCommand = new RelayCommand(DeletePreset);
        ResetGammaCommand = new RelayCommand(() => Gamma = ColorAdjustment.Neutral);
        ResetGpuCommand = new RelayCommand(ResetGpu);
        UnlockRangeCommand = new RelayCommand(UnlockRange);
        ApplyInputCommand = new AsyncCommand(ApplyInputAsync);
        ReloadCommand = new RelayCommand(Reload);
        Reload();
    }

    private void Reload()
    {
        Displays = DisplayManager.GetDisplays();
        OnPropertyChanged(nameof(Displays));
        SelectedDisplay = Displays.FirstOrDefault(d => d.DeviceName == _selected?.DeviceName) ?? Displays.FirstOrDefault();
        _ = LoadDdcMonitorsAsync();
    }

    private DisplayColorSettings? SavedForSelected
    {
        get
        {
            if (_selected is null) return null;
            var all = _services.Settings.Current.Displays;
            if (!all.TryGetValue(_selected.DeviceName, out var s)) all[_selected.DeviceName] = s = new DisplayColorSettings();
            return s;
        }
    }

    private void LoadDisplay()
    {
        _loading = true;
        Gamma = SavedForSelected?.Gamma ?? ColorAdjustment.Neutral;
        _loading = false;
        OnGammaChanged();

        GpuProperties.Clear();
        if (_selected is not null)
        {
            foreach (var gpu in _services.GpuColor.Where(g => g.Displays.Contains(_selected.DeviceName, StringComparer.OrdinalIgnoreCase)))
                foreach (var property in gpu.SupportedProperties)
                    if (gpu.Get(_selected.DeviceName, property) is { } range)
                        GpuProperties.Add(new GpuPropertyViewModel(gpu, _selected.DeviceName, property, range, OnGpuValueChanged));
        }
        OnPropertyChanged(nameof(HasGpuControls));
        PopulateDdcControls();
        RefreshPresets();
    }

    // ---- Gamma ramp ---------------------------------------------------------

    public ColorAdjustment Gamma
    {
        get => _gamma;
        set
        {
            if (!Set(ref _gamma, value)) return;
            OnGammaChanged();
            if (_loading || _selected is null) return;
            MarkCustom();

            var device = _selected.DeviceName;
            var saved = SavedForSelected!;
            _gammaDebounce.Run(() =>
            {
                if (!GammaController.Apply(device, value))
                    _services.Report("Windows refused this colour change. Unlock the full gamma range below, or use smaller adjustments.");
                saved.Gamma = value;
                _services.SaveSettings();
            });
        }
    }

    private void OnGammaChanged()
    {
        OnPropertyChanged(nameof(GammaValue));
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(Temperature));
        OnPropertyChanged(nameof(RedGain));
        OnPropertyChanged(nameof(GreenGain));
        OnPropertyChanged(nameof(BlueGain));
    }

    public double GammaValue { get => _gamma.Gamma; set => Gamma = _gamma with { Gamma = value }; }
    public double Brightness { get => _gamma.Brightness * 100; set => Gamma = _gamma with { Brightness = value / 100 }; }
    public double Contrast { get => _gamma.Contrast * 100; set => Gamma = _gamma with { Contrast = value / 100 }; }
    public double Temperature { get => _gamma.Temperature; set => Gamma = _gamma with { Temperature = (int)value }; }
    public double RedGain { get => _gamma.RedGain * 100; set => Gamma = _gamma with { RedGain = value / 100 }; }
    public double GreenGain { get => _gamma.GreenGain * 100; set => Gamma = _gamma with { GreenGain = value / 100 }; }
    public double BlueGain { get => _gamma.BlueGain * 100; set => Gamma = _gamma with { BlueGain = value / 100 }; }

    private void UnlockRange()
    {
        if (!AppServices.IsAdministrator)
        {
            _services.Report("Unlocking the gamma range needs administrator rights. Use Settings → Restart as administrator.");
            return;
        }
        GammaController.UnlockFullRange();
        OnPropertyChanged(nameof(FullRangeUnlocked));
        _services.Report("Full gamma range unlocked. Sign out and back in for it to take effect.");
    }

    // ---- GPU driver ---------------------------------------------------------

    private void OnGpuValueChanged(GpuPropertyViewModel p)
    {
        SavedForSelected!.Gpu[p.Property] = p.Value;
        MarkCustom();
        _services.SaveSettings();
    }

    private void ResetGpu()
    {
        foreach (var p in GpuProperties) p.Value = p.Default;
        SavedForSelected?.Gpu.Clear();
        _services.SaveSettings();
    }

    // ---- DDC/CI -------------------------------------------------------------

    private async Task LoadDdcMonitorsAsync()
    {
        DdcLoading = true;
        try
        {
            var old = _ddcMonitors;
            _ddcMonitors = await Task.Run(() =>
            {
                foreach (var m in old) m.Dispose();
                return DdcMonitor.EnumerateAll();
            });
            PopulateDdcControls();
        }
        catch (Exception ex)
        {
            _services.Report($"DDC/CI unavailable: {ex.Message}");
        }
        finally
        {
            DdcLoading = false;
        }
    }

    private async void PopulateDdcControls()
    {
        DdcControls.Clear();
        OnPropertyChanged(nameof(HasDdc));
        OnPropertyChanged(nameof(ShowNoDdc));
        var monitor = _ddcMonitors.FirstOrDefault(m => m.DeviceName.Equals(_selected?.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (monitor is null) return;

        (byte Code, string Label)[] codes =
        [
            (VcpCode.Brightness, "Brightness"),
            (VcpCode.Contrast, "Contrast"),
            (VcpCode.RedGain, "Red gain"),
            (VcpCode.GreenGain, "Green gain"),
            (VcpCode.BlueGain, "Blue gain"),
            (VcpCode.Volume, "Speaker volume"),
        ];

        var (values, input) = await Task.Run(async () =>
        {
            await _ddcGate.WaitAsync();
            try
            {
                return (codes.Select(c => (c.Code, c.Label, Value: monitor.Get(c.Code))).ToList(), monitor.Get(VcpCode.InputSource));
            }
            finally
            {
                _ddcGate.Release();
            }
        });

        foreach (var (code, label, value) in values)
            if (value is { Maximum: > 0 })
                DdcControls.Add(new DdcControlViewModel(code, label, value, v => SetVcp(monitor, code, v)));
        SelectedInput = input?.Current & 0xFF;
        OnPropertyChanged(nameof(HasDdc));
        OnPropertyChanged(nameof(ShowNoDdc));
    }

    private void SetVcp(DdcMonitor monitor, byte code, uint value) => Task.Run(async () =>
    {
        await _ddcGate.WaitAsync();
        try
        {
            if (!monitor.Set(code, value)) _services.Report("The monitor did not accept the DDC/CI command.");
        }
        finally
        {
            _ddcGate.Release();
        }
    });

    private async Task ApplyInputAsync()
    {
        var monitor = _ddcMonitors.FirstOrDefault(m => m.DeviceName.Equals(_selected?.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (monitor is null || SelectedInput is not uint input) return;
        await Task.Run(() => monitor.Set(VcpCode.InputSource, input));
        _services.Report($"Switched input to {InputSources.GetValueOrDefault(input, input.ToString())}.");
    }
}

public sealed class GpuPropertyViewModel : ObservableObject
{
    private readonly IGpuColorControl _gpu;
    private readonly string _device;
    private readonly Action<GpuPropertyViewModel> _changed;
    private int _value;

    public GpuPropertyViewModel(IGpuColorControl gpu, string device, GpuColorProperty property, GpuColorRange range,
        Action<GpuPropertyViewModel> changed)
    {
        _gpu = gpu;
        _device = device;
        _changed = changed;
        Property = property;
        Min = range.Min;
        Max = range.Max;
        Default = range.Default;
        _value = range.Current;
    }

    public GpuColorProperty Property { get; }
    public int Min { get; }
    public int Max { get; }
    public int Default { get; }

    public string Label => (_gpu.Vendor, Property) switch
    {
        ("NVIDIA", GpuColorProperty.Saturation) => "Digital vibrance",
        (_, GpuColorProperty.Saturation) => "Saturation",
        _ => Property.ToString(),
    };

    public int Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value)) return;
            if (_gpu.Set(_device, Property, value)) _changed(this);
            else AppServices.Instance.Report($"The {_gpu.Vendor} driver refused the {Label.ToLowerInvariant()} change.");
        }
    }
}

public sealed class DdcControlViewModel : ObservableObject
{
    private readonly Action<uint> _set;
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(150));
    private double _value;

    public DdcControlViewModel(byte code, string label, VcpValue value, Action<uint> set)
    {
        Code = code;
        Label = label;
        Maximum = value.Maximum;
        _value = value.Current;
        _set = set;
    }

    public byte Code { get; }
    public string Label { get; }
    public double Maximum { get; }

    public double Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value)) _debounce.Run(() => _set((uint)Math.Round(value)));
        }
    }
}

public sealed class PresetItemViewModel(ColorPreset preset, bool isActive)
{
    public ColorPreset Preset { get; } = preset;
    public string Name => Preset.Name;
    public bool IsBuiltIn => Preset.IsBuiltIn;
    public bool IsActive { get; } = isActive;

    public string Swatch
    {
        get
        {
            var (r, g, b) = ColorPresetMath.Swatch(Preset);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (Preset.Gamma.Temperature != 6500) parts.Add($"{Preset.Gamma.Temperature} K");
            if (Preset.Saturation is double s && Math.Abs(s) > 0.005) parts.Add($"vibrance {(s > 0 ? "+" : "")}{s * 100:0}%");
            if (Math.Abs(Preset.Gamma.Gamma - 1) > 0.005) parts.Add($"gamma {Preset.Gamma.Gamma:0.00}");
            if (Preset.MonitorBrightness is double mb) parts.Add($"monitor {mb * 100:0}%");
            return parts.Count == 0 ? "Neutral" : string.Join(" · ", parts);
        }
    }
}
