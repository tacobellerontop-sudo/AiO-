using System.Collections.ObjectModel;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.Razer;
using AiO.Core.Settings;

namespace AiO.App.ViewModels;

public sealed class RazerViewModel : ObservableObject
{
    private bool _scanning;
    private bool _scanned;

    public ObservableCollection<RazerDeviceViewModel> Devices { get; } = [];

    public bool Scanning { get => _scanning; private set => Set(ref _scanning, value); }
    public bool NoDevices => _scanned && Devices.Count == 0;

    public ICommand ScanCommand { get; }

    public RazerViewModel()
    {
        ScanCommand = new AsyncCommand(ScanAsync);
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        Scanning = true;
        try
        {
            foreach (var d in Devices) d.Dispose();
            Devices.Clear();
            var found = await Task.Run(RazerDevice.DiscoverAll);
            foreach (var device in found) Devices.Add(new RazerDeviceViewModel(device));
            foreach (var d in Devices) _ = d.LoadAsync();
        }
        finally
        {
            Scanning = false;
            _scanned = true;
            OnPropertyChanged(nameof(NoDevices));
        }
    }
}

/// <summary>One DPI stage in the editor.</summary>
public sealed class DpiStageViewModel(int number, int dpi) : ObservableObject
{
    private int _dpi = dpi;

    public int Number { get; } = number;
    public string Label => $"Stage {Number}";
    public double Dpi { get => _dpi; set => Set(ref _dpi, (int)Math.Round(value / 50) * 50); }
}

public sealed class RazerDeviceViewModel : ObservableObject, IDisposable
{
    private readonly RazerDevice _device;
    private readonly RazerDeviceSettings _saved;
    private readonly Debouncer _brightnessDebounce = new(TimeSpan.FromMilliseconds(120));
    private int _dpi = 800;
    private int? _pollingRate;
    private string _battery = "";
    private IReadOnlyList<RazerLed> _leds;
    private IReadOnlyList<int> _pollingRates;
    private RazerLed _selectedLed;
    private RazerEffect _selectedEffect;
    private string _color;
    private string _color2;
    private double _brightness;
    private bool _loaded;
    private int _activeStage = 1;
    private double _idleMinutes = 5;
    private double _lowBattery = 10;
    private bool _hasIdle;
    private bool _hasLowBattery;

    public RazerDeviceViewModel(RazerDevice device)
    {
        _device = device;
        var all = AppServices.Instance.Settings.Current.Razer;
        string key = device.ProductId.ToString("X4");
        if (!all.TryGetValue(key, out var saved)) all[key] = saved = new RazerDeviceSettings();
        _saved = saved;
        _saved.Name = device.Name;

        _leds = device.SuggestedLeds;
        _pollingRates = device.PollingRates;
        _selectedLed = _leds[0];
        _selectedEffect = Enum.TryParse<RazerEffect>(saved.Effect, out var e) ? e : RazerEffect.Static;
        _color = saved.Color;
        _color2 = saved.Color2;
        _brightness = saved.Brightness;

        ApplyDpiCommand = new AsyncCommand(() => Run(() => _device.SetDpi(Dpi, Dpi), $"DPI set to {Dpi}.", () => _saved.Dpi = Dpi));
        SetDpiPresetCommand = new RelayCommand(p => { if (p is string s && int.TryParse(s, out int v)) Dpi = v; });
        ApplyLightingCommand = new AsyncCommand(ApplyLightingAsync);
        AddStageCommand = new RelayCommand(AddStage, () => DpiStages.Count < RazerCommands.MaxDpiStages);
        RemoveStageCommand = new RelayCommand(RemoveStage, () => DpiStages.Count > 1);
        ApplyStagesCommand = new AsyncCommand(ApplyStagesAsync);
        ApplyPowerCommand = new AsyncCommand(ApplyPowerAsync);
    }

    public string Name => _device.Name;
    public string Details => $"{_device.Type} · firmware {_device.FirmwareVersion} · USB PID {_device.ProductId:X4}{_battery}";
    public bool IsMouse => _device.Type is RazerDeviceType.Mouse or RazerDeviceType.Unknown;

    // ---- Sensor -----------------------------------------------------------------

    public int Dpi { get => _dpi; set => Set(ref _dpi, Math.Clamp(value, 100, 45000)); }
    public IReadOnlyList<int> PollingRates { get => _pollingRates; private set => Set(ref _pollingRates, value); }

    public int? PollingRate
    {
        get => _pollingRate;
        set
        {
            if (!Set(ref _pollingRate, value) || !_loaded || value is not int hz) return;
            _ = Run(() => _device.SetPollingRate(hz), $"Polling rate set to {hz} Hz.", () => _saved.PollingRate = hz);
        }
    }

    public ObservableCollection<DpiStageViewModel> DpiStages { get; } = [];
    public bool HasDpiStages => DpiStages.Count > 0;
    public IReadOnlyList<int> StageNumbers => Enumerable.Range(1, DpiStages.Count).ToList();
    public int ActiveStage { get => _activeStage; set => Set(ref _activeStage, value); }

    public ICommand ApplyDpiCommand { get; }
    public ICommand SetDpiPresetCommand { get; }
    public ICommand AddStageCommand { get; }
    public ICommand RemoveStageCommand { get; }
    public ICommand ApplyStagesCommand { get; }

    private void AddStage()
    {
        int last = DpiStages.Count > 0 ? (int)DpiStages[^1].Dpi : 800;
        DpiStages.Add(new DpiStageViewModel(DpiStages.Count + 1, Math.Min(last * 2, 26000)));
        OnPropertyChanged(nameof(StageNumbers));
    }

    private void RemoveStage()
    {
        DpiStages.RemoveAt(DpiStages.Count - 1);
        if (ActiveStage > DpiStages.Count) ActiveStage = DpiStages.Count;
        OnPropertyChanged(nameof(StageNumbers));
    }

    private Task ApplyStagesAsync()
    {
        var stages = DpiStages.Select(s => (int)s.Dpi).ToList();
        int active = ActiveStage;
        return Run(() => _device.SetDpiStages(stages, active), $"{stages.Count} DPI stages saved to {Name}.", () => { });
    }

    // ---- Power (wireless) ---------------------------------------------------------

    public bool HasIdle { get => _hasIdle; private set => Set(ref _hasIdle, value); }
    public bool HasLowBattery { get => _hasLowBattery; private set => Set(ref _hasLowBattery, value); }
    public bool HasPower => HasIdle || HasLowBattery;
    public double IdleMinutes { get => _idleMinutes; set => Set(ref _idleMinutes, value); }
    public double LowBattery { get => _lowBattery; set => Set(ref _lowBattery, value); }
    public ICommand ApplyPowerCommand { get; }

    private Task ApplyPowerAsync()
    {
        int idle = (int)(IdleMinutes * 60), low = (int)LowBattery;
        bool hasIdle = HasIdle, hasLow = HasLowBattery;
        return Run(() =>
        {
            if (hasIdle) _device.SetIdleSeconds(idle);
            if (hasLow) _device.SetLowBatteryPercent(low);
        }, "Power settings saved to the device.", () => { });
    }

    // ---- Lighting -------------------------------------------------------------------

    public IReadOnlyList<RazerLed> Leds { get => _leds; private set => Set(ref _leds, value); }
    public RazerLed SelectedLed { get => _selectedLed; set => Set(ref _selectedLed, value); }

    public IReadOnlyList<RazerEffect> Effects { get; } =
        [RazerEffect.Static, RazerEffect.Breathing, RazerEffect.Spectrum, RazerEffect.Wave, RazerEffect.Reactive, RazerEffect.Off];

    public RazerEffect SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (!Set(ref _selectedEffect, value)) return;
            OnPropertyChanged(nameof(UsesColor));
            OnPropertyChanged(nameof(UsesSecondColor));
        }
    }

    public bool UsesColor => SelectedEffect is RazerEffect.Static or RazerEffect.Breathing or RazerEffect.Reactive;
    public bool UsesSecondColor => SelectedEffect is RazerEffect.Breathing;

    public string Color { get => _color; set => Set(ref _color, value); }
    public string Color2 { get => _color2; set => Set(ref _color2, value); }

    /// <summary>Apply the effect to every zone at once.</summary>
    public bool AllZones { get; set; } = true;

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!Set(ref _brightness, value) || !_loaded) return;
            var leds = AllZones ? Leds : [SelectedLed];
            _brightnessDebounce.Run(() => _ = Run(() =>
            {
                foreach (var led in leds) _device.SetBrightness(led, (byte)value);
            }, null, () => _saved.Brightness = (int)value));
        }
    }

    public ICommand ApplyLightingCommand { get; }

    public async Task LoadAsync()
    {
        var info = await Task.Run(() => new
        {
            Dpi = _device.GetDpi(),
            Rate = _device.GetPollingRate(),
            Battery = _device.GetBatteryPercent(),
            Charging = _device.IsCharging(),
            Leds = _device.DetectLeds(),
            Stages = _device.GetDpiStages(),
            Idle = _device.GetIdleSeconds(),
            LowBattery = _device.GetLowBatteryPercent(),
        });

        Leds = info.Leds;
        if (!Leds.Contains(SelectedLed)) SelectedLed = Leds[0];
        PollingRates = _device.PollingRates;
        if (info.Dpi is { } d) Dpi = d.X;
        if (info.Rate is int r and > 0) PollingRate = r;
        if (_device.GetBrightness(SelectedLed) is int b) Brightness = b;
        if (info.Battery is int pct) _battery = $" · battery {pct}%{(info.Charging == true ? " (charging)" : "")}";

        if (info.Stages is { } stages)
        {
            DpiStages.Clear();
            for (int i = 0; i < stages.Stages.Count; i++) DpiStages.Add(new DpiStageViewModel(i + 1, stages.Stages[i]));
            ActiveStage = Math.Clamp(stages.Active, 1, Math.Max(1, DpiStages.Count));
            OnPropertyChanged(nameof(HasDpiStages));
            OnPropertyChanged(nameof(StageNumbers));
        }

        // Only wireless devices answer the power commands.
        if (info.Idle is int idle and > 0) { IdleMinutes = Math.Round(idle / 60.0); HasIdle = true; }
        if (info.LowBattery is int low and > 0) { LowBattery = low; HasLowBattery = true; }
        OnPropertyChanged(nameof(HasPower));
        OnPropertyChanged(nameof(Details));
        _loaded = true;
    }

    private async Task ApplyLightingAsync()
    {
        var colors = new List<RgbColor>();
        if (UsesColor) colors.Add(RgbColor.FromHex(Color));
        if (UsesSecondColor) colors.Add(RgbColor.FromHex(Color2));
        var leds = AllZones ? Leds : [SelectedLed];
        var effect = SelectedEffect;
        await Run(() =>
        {
            // Some zones support fewer effects; one refusing shouldn't stop the others.
            int ok = 0;
            RazerCommandException? last = null;
            foreach (var led in leds)
            {
                try { _device.SetEffect(led, effect, colors); ok++; }
                catch (RazerCommandException ex) { last = ex; }
            }
            if (ok == 0 && last is not null) throw last;
        }, $"{effect} applied to {Name}.", () =>
        {
            _saved.Effect = effect.ToString();
            _saved.Color = Color;
            _saved.Color2 = Color2;
        });
    }

    /// <summary>Runs a device command off the UI thread, reports the outcome and saves on success.</summary>
    private static async Task Run(Action command, string? success, Action save)
    {
        try
        {
            await Task.Run(command);
            save();
            AppServices.Instance.SaveSettings();
            if (success is not null) AppServices.Instance.Report(success);
        }
        catch (RazerCommandException ex)
        {
            AppServices.Instance.Report($"Razer: {ex.Message}. This setting may not exist on this device.");
        }
        catch (Exception ex)
        {
            AppServices.Instance.Report($"Razer: {ex.Message}");
        }
    }

    public void Dispose() => _device.Dispose();
}
