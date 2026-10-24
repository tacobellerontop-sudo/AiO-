using System.Collections.ObjectModel;
using System.Windows.Input;
using AiO.App.Infrastructure;
using AiO.Core.OpenRgb;
using AiO.Core.Razer;
using AiO.Core.Settings;

namespace AiO.App.ViewModels;

public sealed class RazerViewModel : ObservableObject
{
    private readonly AppServices _services = AppServices.Instance;
    private bool _scanning;
    private bool _scanned;
    private OpenRgbClient? _openRgb;
    private string _openRgbStatus = "Not connected";

    public ObservableCollection<RazerDeviceViewModel> Devices { get; } = [];
    public ObservableCollection<OpenRgbControllerViewModel> Controllers { get; } = [];

    public bool Scanning { get => _scanning; private set => Set(ref _scanning, value); }
    public bool NoDevices => _scanned && Devices.Count == 0;

    public OpenRgbSettings OpenRgb => _services.Settings.Current.OpenRgb;
    public string OpenRgbStatus { get => _openRgbStatus; private set => Set(ref _openRgbStatus, value); }

    public ICommand ScanCommand { get; }
    public ICommand ConnectOpenRgbCommand { get; }

    public RazerViewModel()
    {
        ScanCommand = new AsyncCommand(ScanAsync);
        ConnectOpenRgbCommand = new AsyncCommand(ConnectOpenRgbAsync);
        _ = ScanAsync();
        if (OpenRgb.AutoConnect) _ = ConnectOpenRgbAsync();
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

    private async Task ConnectOpenRgbAsync()
    {
        if (_openRgb is not null) await _openRgb.DisposeAsync();
        Controllers.Clear();
        OpenRgbStatus = "Connecting…";
        try
        {
            _openRgb = new OpenRgbClient();
            await _openRgb.ConnectAsync(OpenRgb.Host, OpenRgb.Port);
            var controllers = await _openRgb.GetControllersAsync();
            foreach (var c in controllers) Controllers.Add(new OpenRgbControllerViewModel(_openRgb, c));
            OpenRgbStatus = $"Connected (protocol {_openRgb.ProtocolVersion}) · {controllers.Count} device(s)";
            _services.SaveSettings();
        }
        catch (Exception ex)
        {
            OpenRgbStatus = $"Could not connect: {ex.Message}. Start OpenRGB and enable its SDK server.";
            _openRgb = null;
        }
    }
}

public sealed class RazerDeviceViewModel : ObservableObject, IDisposable
{
    private readonly RazerDevice _device;
    private readonly RazerDeviceSettings _saved;
    private readonly Debouncer _brightnessDebounce = new(TimeSpan.FromMilliseconds(120));
    private int _dpi = 800;
    private int? _pollingRate;
    private string _battery = "";
    private RazerLed _selectedLed;
    private RazerEffect _selectedEffect;
    private string _color;
    private string _color2;
    private double _brightness;
    private bool _loaded;

    public RazerDeviceViewModel(RazerDevice device)
    {
        _device = device;
        var all = AppServices.Instance.Settings.Current.Razer;
        string key = device.ProductId.ToString("X4");
        if (!all.TryGetValue(key, out var saved)) all[key] = saved = new RazerDeviceSettings();
        _saved = saved;
        _saved.Name = device.Name;

        _selectedLed = device.SuggestedLeds[0];
        _selectedEffect = Enum.TryParse<RazerEffect>(saved.Effect, out var e) ? e : RazerEffect.Static;
        _color = saved.Color;
        _color2 = saved.Color2;
        _brightness = saved.Brightness;

        ApplyDpiCommand = new AsyncCommand(() => Run(() => _device.SetDpi(Dpi, Dpi), $"DPI set to {Dpi}.", () => _saved.Dpi = Dpi));
        SetDpiPresetCommand = new RelayCommand(p => { if (p is string s && int.TryParse(s, out int v)) Dpi = v; });
        ApplyLightingCommand = new AsyncCommand(ApplyLightingAsync);
    }

    public string Name => _device.Name;
    public string Details => $"{_device.Type} · firmware {_device.FirmwareVersion} · USB PID {_device.ProductId:X4}{_battery}";
    public bool IsMouse => _device.Type is RazerDeviceType.Mouse or RazerDeviceType.Unknown;

    public int Dpi { get => _dpi; set => Set(ref _dpi, Math.Clamp(value, 100, 45000)); }
    public IReadOnlyList<int> PollingRates { get; } = [125, 500, 1000];

    public int? PollingRate
    {
        get => _pollingRate;
        set
        {
            if (!Set(ref _pollingRate, value) || !_loaded || value is not int hz) return;
            _ = Run(() => _device.SetPollingRate(hz), $"Polling rate set to {hz} Hz.", () => _saved.PollingRate = hz);
        }
    }

    public IReadOnlyList<RazerLed> Leds => _device.SuggestedLeds;
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

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!Set(ref _brightness, value) || !_loaded) return;
            var led = SelectedLed;
            _brightnessDebounce.Run(() => _ = Run(() => _device.SetBrightness(led, (byte)value), null, () => _saved.Brightness = (int)value));
        }
    }

    public ICommand ApplyDpiCommand { get; }
    public ICommand SetDpiPresetCommand { get; }
    public ICommand ApplyLightingCommand { get; }

    public async Task LoadAsync()
    {
        var (dpi, rate, battery, charging, brightness) = await Task.Run(() =>
            (_device.GetDpi(), _device.GetPollingRate(), _device.GetBatteryPercent(), _device.IsCharging(), _device.GetBrightness(SelectedLed)));

        if (dpi is { } d) Dpi = d.X;
        if (rate is int r and > 0) PollingRate = r;
        if (brightness is int b) Brightness = b;
        if (battery is int pct) _battery = $" · battery {pct}%{(charging == true ? " (charging)" : "")}";
        OnPropertyChanged(nameof(Details));
        _loaded = true;
    }

    private async Task ApplyLightingAsync()
    {
        var colors = new List<RgbColor>();
        if (UsesColor) colors.Add(RgbColor.FromHex(Color));
        if (UsesSecondColor) colors.Add(RgbColor.FromHex(Color2));
        var led = SelectedLed;
        var effect = SelectedEffect;
        await Run(() => _device.SetEffect(led, effect, colors), $"{effect} applied to {Name}.", () =>
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
            AppServices.Instance.Report($"Razer: {ex.Message}. This zone or setting may not exist on this device.");
        }
    }

    public void Dispose() => _device.Dispose();
}

public sealed class OpenRgbControllerViewModel : ObservableObject
{
    private readonly OpenRgbClient _client;
    private readonly OpenRgbController _controller;
    private string _color = "#FFFFFF";
    private OpenRgbMode? _selectedMode;

    public OpenRgbControllerViewModel(OpenRgbClient client, OpenRgbController controller)
    {
        _client = client;
        _controller = controller;
        _selectedMode = controller.Modes.ElementAtOrDefault(controller.ActiveMode);
        ApplyColorCommand = new AsyncCommand(async () =>
        {
            await _client.SetColorAsync(_controller, RgbColor.FromHex(Color));
            AppServices.Instance.Report($"{Name}: colour set.");
        });
        ApplyModeCommand = new AsyncCommand(async () =>
        {
            if (SelectedMode is null) return;
            await _client.SetModeAsync(_controller, _controller.Modes.IndexOf(SelectedMode), RgbColor.FromHex(Color));
            AppServices.Instance.Report($"{Name}: {SelectedMode.Name} mode.");
        });
    }

    public string Name => _controller.Name;
    public string Details => $"{_controller.Vendor} · {_controller.LedNames.Count} LEDs · {string.Join(", ", _controller.Zones.Select(z => z.Name))}";
    public IReadOnlyList<OpenRgbMode> Modes => _controller.Modes;
    public OpenRgbMode? SelectedMode { get => _selectedMode; set => Set(ref _selectedMode, value); }
    public string Color { get => _color; set => Set(ref _color, value); }

    public ICommand ApplyColorCommand { get; }
    public ICommand ApplyModeCommand { get; }
}
