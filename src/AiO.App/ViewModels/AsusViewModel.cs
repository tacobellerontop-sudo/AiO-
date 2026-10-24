using System.Windows.Input;
using System.Windows.Threading;
using AiO.App.Infrastructure;
using AiO.App.Views;
using AiO.Core.Asus;
using AiO.Core.Razer;
using AiO.Core.Settings;

namespace AiO.App.ViewModels;

public sealed class AsusViewModel : ObservableObject
{
    private readonly AsusLaptop _asus;
    private readonly AppServices _services = AppServices.Instance;
    private readonly DispatcherTimer _sensorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private AsusGpuMode? _gpuMode;
    private string _gpuMessage = "";
    private string _sensorText = "";
    private AsusPerformanceMode _editingMode;
    private FanCurve _cpuCurve = FanCurve.Default(AsusPerformanceMode.Balanced);
    private FanCurve _gpuCurve = FanCurve.Default(AsusPerformanceMode.Balanced);
    private FanCurve _midCurve = FanCurve.Default(AsusPerformanceMode.Balanced);
    private double _batteryLimit;
    private bool? _overdrive;

    private AsusSettings Saved => _services.Settings.Current.Asus;

    public AsusViewModel(AsusLaptop asus)
    {
        _asus = asus;
        // Probe each feature separately: one the laptop lacks must not take the whole page down.
        _gpuMode = Probe(asus.GetGpuMode, null);
        _batteryLimit = Saved.BatteryLimit;
        _overdrive = Probe(asus.GetOverdrive, null);
        _editingMode = Saved.PerformanceMode;
        AuraAvailable = Probe(AsusAura.IsAvailable, false);

        _sensorTimer.Tick += (_, _) => ReadSensors();

        RestartCommand = new RelayCommand(SystemIntegration.RestartWindows);
        ApplyBatteryCommand = new RelayCommand(ApplyBattery);
        BatteryPresetCommand = new RelayCommand(p => { if (p is string s && int.TryParse(s, out int v)) { BatteryLimit = v; ApplyBattery(); } });
        LoadFirmwareCurvesCommand = new RelayCommand(LoadFirmwareCurves);
        ApplyFansCommand = new RelayCommand(ApplyFans);
        ApplyPowerCommand = new RelayCommand(ApplyPower);
        ApplyKeyboardCommand = new RelayCommand(ApplyKeyboard);

        LoadEditingProfile();
    }

    private static T Probe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    public void StartSensors()
    {
        ReadSensors();
        _sensorTimer.Start();
    }

    public void StopSensors() => _sensorTimer.Stop();

    // ---- Performance mode ---------------------------------------------------

    public IReadOnlyList<AsusPerformanceMode> Modes { get; } = [AsusPerformanceMode.Silent, AsusPerformanceMode.Balanced, AsusPerformanceMode.Turbo];

    public AsusPerformanceMode PerformanceMode
    {
        get => Saved.PerformanceMode;
        set
        {
            if (Saved.PerformanceMode == value) return;
            Saved.PerformanceMode = value;
            OnPropertyChanged();
            ApplyPerformanceMode();
            EditingMode = value;
        }
    }

    public void ApplyPerformanceMode()
    {
        try
        {
            StartupApplier.ApplyPerformanceMode(_asus, Saved, Saved.PerformanceMode);
            _services.SaveSettings();
            _services.Report($"{Saved.PerformanceMode} mode.");
        }
        catch (Exception ex)
        {
            _services.Report($"Could not switch mode: {ex.Message}");
        }
        OnPropertyChanged(nameof(PerformanceMode));
    }

    public bool SyncWindowsPowerMode
    {
        get => Saved.SyncWindowsPowerMode;
        set { Saved.SyncWindowsPowerMode = value; _services.SaveSettings(); OnPropertyChanged(); }
    }

    // ---- GPU mode -----------------------------------------------------------

    public bool SupportsGpuModes => _asus.SupportsGpuModes;
    public bool SupportsUltimate => _asus.SupportsMux;
    public string GpuMessage { get => _gpuMessage; private set => Set(ref _gpuMessage, value); }
    public bool RestartPending { get; private set; }
    public ICommand RestartCommand { get; }

    public AsusGpuMode? GpuMode
    {
        get => _gpuMode;
        set
        {
            if (value is not AsusGpuMode target || target == _gpuMode) return;
            bool needsReboot = target == AsusGpuMode.Ultimate || _gpuMode == AsusGpuMode.Ultimate;
            if (needsReboot && !Dialogs.Confirm($"Switching to {target} changes the MUX switch and needs a restart. Continue?"))
            {
                OnPropertyChanged();
                return;
            }

            var result = _asus.SetGpuMode(target);
            GpuMessage = result.Message;
            RestartPending |= result.RebootRequired;
            OnPropertyChanged(nameof(RestartPending));
            _gpuMode = result.RebootRequired ? target : _asus.GetGpuMode();
            OnPropertyChanged();
        }
    }

    // ---- Sensors ------------------------------------------------------------

    public string SensorText { get => _sensorText; private set => Set(ref _sensorText, value); }

    private void ReadSensors()
    {
        try
        {
            var s = _asus.ReadSensors();
            static string T(int? v) => v is int t ? $"{t} °C" : "–";
            static string F(int? v) => v is int r ? $"{r} RPM" : "–";
            SensorText = $"CPU {T(s.CpuTemperature)} · fan {F(s.CpuFanRpm)}     GPU {T(s.GpuTemperature)} · fan {F(s.GpuFanRpm)}" +
                         (s.MidFanRpm is not null ? $"     Mid fan {F(s.MidFanRpm)}" : "");
        }
        catch (Exception ex)
        {
            SensorText = $"Sensors unavailable: {ex.Message}";
            _sensorTimer.Stop();
        }
    }

    // ---- Battery and screen ------------------------------------------------

    public bool SupportsBatteryLimit => _asus.SupportsBatteryLimit;
    public double BatteryLimit { get => _batteryLimit; set => Set(ref _batteryLimit, value); }
    public ICommand ApplyBatteryCommand { get; }
    public ICommand BatteryPresetCommand { get; }

    private void ApplyBattery()
    {
        int limit = (int)BatteryLimit;
        if (_asus.SetBatteryLimit(limit))
        {
            Saved.BatteryLimit = limit;
            _services.SaveSettings();
            _services.Report($"Battery will stop charging at {limit}%.");
        }
        else
        {
            _services.Report("The firmware refused the charge limit.");
        }
    }

    public bool SupportsOverdrive => _overdrive is not null;

    public bool Overdrive
    {
        get => _overdrive == true;
        set
        {
            if (!_asus.SetOverdrive(value)) { _services.Report("The firmware refused the overdrive change."); return; }
            _overdrive = value;
            Saved.PanelOverdrive = value;
            _services.SaveSettings();
            OnPropertyChanged();
        }
    }

    // ---- Fan curves and power limits (per mode) -----------------------------

    public bool SupportsFanCurves => _asus.SupportsFanCurves;
    public bool SupportsMidFan => _asus.SupportsMidFan;
    public bool SupportsPowerLimits => _asus.SupportsPowerLimits;
    public bool SupportsGpuPowerTuning => _asus.SupportsGpuPowerTuning;

    /// <summary>The mode whose fan curves and power limits are being edited.</summary>
    public AsusPerformanceMode EditingMode
    {
        get => _editingMode;
        set
        {
            if (Set(ref _editingMode, value)) LoadEditingProfile();
        }
    }

    private AsusModeProfile Profile => Saved.Profile(EditingMode);

    public bool CustomFanCurves { get => Profile.CustomFanCurves; set { Profile.CustomFanCurves = value; OnPropertyChanged(); } }
    public FanCurve CpuCurve { get => _cpuCurve; private set => Set(ref _cpuCurve, value); }
    public FanCurve GpuCurve { get => _gpuCurve; private set => Set(ref _gpuCurve, value); }
    public FanCurve MidCurve { get => _midCurve; private set => Set(ref _midCurve, value); }

    public bool CustomPowerLimits { get => Profile.CustomPowerLimits; set { Profile.CustomPowerLimits = value; OnPropertyChanged(); } }
    public double SustainedWatts { get => Profile.SustainedWatts; set { Profile.SustainedWatts = (int)value; OnPropertyChanged(); } }
    public double SlowWatts { get => Profile.SlowWatts; set { Profile.SlowWatts = (int)value; OnPropertyChanged(); } }
    public double FastWatts { get => Profile.FastWatts; set { Profile.FastWatts = (int)value; OnPropertyChanged(); } }
    public double GpuDynamicBoost { get => Profile.GpuDynamicBoost; set { Profile.GpuDynamicBoost = (int)value; OnPropertyChanged(); } }
    public double GpuTempTarget { get => Profile.GpuTempTarget; set { Profile.GpuTempTarget = (int)value; OnPropertyChanged(); } }

    public ICommand LoadFirmwareCurvesCommand { get; }
    public ICommand ApplyFansCommand { get; }
    public ICommand ApplyPowerCommand { get; }

    private void LoadEditingProfile()
    {
        var p = Profile;
        CpuCurve = p.CpuCurve?.ToCurve() is { } c && c.IsValid() ? c : FirmwareOrDefault(AsusFan.Cpu);
        GpuCurve = p.GpuCurve?.ToCurve() is { } g && g.IsValid() ? g : FirmwareOrDefault(AsusFan.Gpu);
        MidCurve = p.MidCurve?.ToCurve() is { } m && m.IsValid() ? m : FirmwareOrDefault(AsusFan.Mid);
        foreach (var name in new[] { nameof(CustomFanCurves), nameof(CustomPowerLimits), nameof(SustainedWatts), nameof(SlowWatts),
                     nameof(FastWatts), nameof(GpuDynamicBoost), nameof(GpuTempTarget) })
            OnPropertyChanged(name);
    }

    private FanCurve FirmwareOrDefault(AsusFan fan)
    {
        try
        {
            if (SupportsFanCurves && _asus.GetFanCurve(fan, EditingMode) is { } curve) return curve;
        }
        catch
        {
            // Fall through to AiO's defaults.
        }
        return FanCurve.Default(EditingMode);
    }

    private void LoadFirmwareCurves()
    {
        CpuCurve = FirmwareOrDefault(AsusFan.Cpu);
        GpuCurve = FirmwareOrDefault(AsusFan.Gpu);
        MidCurve = FirmwareOrDefault(AsusFan.Mid);
        _services.Report($"Loaded the built-in {EditingMode} curves. Apply to keep them.");
    }

    private void ApplyFans()
    {
        Profile.CpuCurve = FanCurveSetting.From(CpuCurve);
        Profile.GpuCurve = FanCurveSetting.From(GpuCurve);
        Profile.MidCurve = SupportsMidFan ? FanCurveSetting.From(MidCurve) : null;
        SaveAndReapplyIfActive("Fan curves");
    }

    private void ApplyPower() => SaveAndReapplyIfActive("Power limits");

    /// <summary>Saves the profile; if it belongs to the active mode, re-applies the mode so it takes effect now.</summary>
    private void SaveAndReapplyIfActive(string what)
    {
        _services.SaveSettings();
        if (EditingMode == PerformanceMode)
        {
            ApplyPerformanceMode();
            _services.Report($"{what} applied to {EditingMode}.");
        }
        else
        {
            _services.Report($"{what} saved for {EditingMode}; they apply when you switch to it.");
        }
    }

    // ---- Keyboard backlight -------------------------------------------------

    public bool AuraAvailable { get; }
    public IReadOnlyList<AuraMode> AuraModes { get; } = Enum.GetValues<AuraMode>();
    public IReadOnlyList<AuraSpeed> AuraSpeeds { get; } = Enum.GetValues<AuraSpeed>();
    public AuraMode AuraMode { get => Saved.AuraMode; set { Saved.AuraMode = value; OnPropertyChanged(); } }
    public string AuraColor { get => Saved.AuraColor; set { Saved.AuraColor = value; OnPropertyChanged(); } }
    public string AuraColor2 { get => Saved.AuraColor2; set { Saved.AuraColor2 = value; OnPropertyChanged(); } }
    public AuraSpeed AuraSpeed { get => Saved.AuraSpeed; set { Saved.AuraSpeed = value; OnPropertyChanged(); } }
    public double KeyboardBrightness { get => Saved.KeyboardBrightness; set { Saved.KeyboardBrightness = (int)value; OnPropertyChanged(); } }
    public ICommand ApplyKeyboardCommand { get; }

    private void ApplyKeyboard()
    {
        try
        {
            if (AuraAvailable)
            {
                AsusAura.SetBrightness(Saved.KeyboardBrightness);
                AsusAura.SetMode(Saved.AuraMode, RgbColor.FromHex(Saved.AuraColor), RgbColor.FromHex(Saved.AuraColor2), Saved.AuraSpeed);
            }
            else
            {
                _asus.SetAcpiKeyboardBrightness(Saved.KeyboardBrightness);
            }
            Saved.ManageKeyboard = true;
            _services.SaveSettings();
            _services.Report("Keyboard lighting applied.");
        }
        catch (Exception ex)
        {
            _services.Report($"Keyboard lighting failed: {ex.Message}");
        }
    }
}
