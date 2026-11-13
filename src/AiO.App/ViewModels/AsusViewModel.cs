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

    /// <summary>M-key and Fn hotkey bindings (shown on the Laptop settings page).</summary>
    public AsusHotkeysViewModel? Hotkeys { get; init; }

    /// <summary>Plug-in / battery switching, refresh rate and backlight timeout (shown on the Laptop settings page).</summary>
    public AsusAutomationViewModel? Automation { get; init; }

    public AsusViewModel(AsusLaptop asus)
    {
        _asus = asus;
        // Probe each feature separately: one the laptop lacks must not take the whole page down.
        _gpuMode = Probe(asus.GetGpuMode, null);
        _batteryLimit = Saved.BatteryLimit;
        _overdrive = Probe(asus.GetOverdrive, null);
        _editingMode = Saved.PerformanceMode;
        // Effects need either the N-KEY HID device (ROG) or ACPI RGB (TUF/Vivobook).
        AuraAvailable = Probe(AsusAura.IsAvailable, false) || Probe(() => asus.SupportsAcpiKeyboardRgb, false);

        _sensorTimer.Tick += (_, _) => ReadSensors();

        RestartCommand = new RelayCommand(SystemIntegration.RestartWindows);
        ApplyBatteryCommand = new RelayCommand(ApplyBattery);
        BatteryPresetCommand = new RelayCommand(p => { if (p is string s && int.TryParse(s, out int v)) { BatteryLimit = v; ApplyBattery(); } });
        LoadFirmwareCurvesCommand = new RelayCommand(LoadFirmwareCurves);
        ApplyFansCommand = new RelayCommand(ApplyFans);
        ApplyPowerCommand = new RelayCommand(ApplyPower);
        ApplyKeyboardCommand = new RelayCommand(ApplyKeyboard);

        LoadEditingProfile();
        if (Saved.DangerZoneUnlocked) RefreshRyzenStatus();
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

    // ---- Danger zone: power limits and CPU undervolting --------------------------------

    public bool DangerZoneUnlocked
    {
        get => Saved.DangerZoneUnlocked;
        set
        {
            Saved.DangerZoneUnlocked = value;
            if (!value) Saved.Ryzen.ApplyAutomatically = false;
            _services.SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RyzenApplyAutomatically));
            if (value) RefreshRyzenStatus();
        }
    }

    private RyzenTuningSettings Ryzen => Saved.Ryzen;
    private string _ryzenStatus = "";
    public string RyzenStatus { get => _ryzenStatus; private set => Set(ref _ryzenStatus, value); }
    private bool _pawnIoMissing;
    public bool PawnIoMissing { get => _pawnIoMissing; private set => Set(ref _pawnIoMissing, value); }
    public bool NeedsAdmin => !AppServices.IsAdministrator;

    public double CurveOffset { get => Ryzen.CurveOffset; set { Ryzen.CurveOffset = (int)value; OnPropertyChanged(); } }
    public bool UseTempLimit
    {
        get => Ryzen.TempLimit is not null;
        set { Ryzen.TempLimit = value ? (int)TempLimit : null; OnPropertyChanged(); }
    }
    private double _tempLimit = 90;
    public double TempLimit
    {
        get => Ryzen.TempLimit ?? _tempLimit;
        set { _tempLimit = value; if (Ryzen.TempLimit is not null) Ryzen.TempLimit = (int)value; OnPropertyChanged(); }
    }
    public bool RyzenApplyAutomatically
    {
        get => Ryzen.ApplyAutomatically;
        set { Ryzen.ApplyAutomatically = value; _services.SaveSettings(); OnPropertyChanged(); }
    }

    public ICommand ApplyRyzenCommand => new RelayCommand(() => RunRyzen(apply: true));
    public ICommand ResetRyzenCommand => new RelayCommand(() => RunRyzen(apply: false));
    public ICommand InstallPawnIoCommand => new RelayCommand(() => SystemIntegration.OpenUrl(RyzenTuning.PawnIoUrl));
    public ICommand RestartAsAdminCommand => new RelayCommand(() =>
    {
        if (SystemIntegration.RestartAsAdministrator()) ((App)System.Windows.Application.Current).ExitApp();
    });

    private void RefreshRyzenStatus()
    {
        Task.Run(() =>
        {
            using var smu = RyzenTuning.Open(out string problem);
            string text = smu is null ? problem
                : smu.Family == AiO.Core.Ryzen.RyzenFamily.Unsupported ? $"Connected, but AiO doesn't know how to tune this CPU ({smu.CodeName})."
                : $"Ready: {smu.CodeName} CPU via PawnIO.";
            bool missing = problem.Contains("PawnIO driver isn't installed");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { RyzenStatus = text; PawnIoMissing = missing; });
        });
    }

    private void RunRyzen(bool apply)
    {
        if (!apply)
        {
            Ryzen.CurveOffset = 0;
            Ryzen.TempLimit = null;
            OnPropertyChanged(nameof(CurveOffset));
            OnPropertyChanged(nameof(UseTempLimit));
        }
        _services.SaveSettings();
        // Resetting sends offset 0 and the stock 100 °C limit so the CPU is back to defaults now, not after a reboot.
        var send = apply ? Ryzen : new RyzenTuningSettings { CurveOffset = 0, TempLimit = AiO.Core.Ryzen.RyzenSmuProtocol.MaxTempLimit };
        Task.Run(() =>
        {
            string result = RyzenTuning.Apply(send);
            _services.Report(result);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => RyzenStatus = result);
        });
    }

    // ---- Slash lid lighting --------------------------------------------------------

    public bool SlashAvailable { get; } = AsusSlash.IsSlashModel(SystemInfo.ReadBios().Model);
    private SlashSettings Slash => Saved.Slash;
    public IReadOnlyList<SlashEffect> SlashEffects { get; } = Enum.GetValues<SlashEffect>();
    public bool SlashOn { get => Slash.On; set { Slash.On = value; OnPropertyChanged(); } }
    public SlashEffect SelectedSlashEffect { get => Slash.Effect; set { Slash.Effect = value; OnPropertyChanged(); } }
    public double SlashBrightness { get => Slash.Brightness; set { Slash.Brightness = (int)value; OnPropertyChanged(); } }
    public double SlashInterval { get => Slash.Interval; set { Slash.Interval = (int)value; OnPropertyChanged(); } }
    public bool SlashOnBattery { get => Slash.OnBattery; set { Slash.OnBattery = value; OnPropertyChanged(); } }
    public bool SlashOnLidClosed { get => Slash.OnLidClosed; set { Slash.OnLidClosed = value; OnPropertyChanged(); } }
    public ICommand ApplySlashCommand => new RelayCommand(ApplySlash);

    private void ApplySlash()
    {
        try
        {
            if (StartupApplier.ApplySlash(Slash))
            {
                Slash.Manage = true;
                _services.SaveSettings();
                _services.Report(Slash.On ? $"Slash lighting: {Slash.Effect}." : "Slash lighting off.");
            }
            else
            {
                _services.Report("The Slash lighting didn't accept the command. Close Armoury Crate or G-Helper and try again.");
            }
        }
        catch (Exception ex)
        {
            _services.Report($"Slash lighting failed: {ex.Message}");
        }
    }

    // ---- Fn-lock, touchpad, camera -------------------------------------------------

    private bool? _supportsFnLock;
    public bool SupportsFnLock => _supportsFnLock ??= Probe(() => _asus.SupportsFnLock, false);

    public bool FnLock
    {
        get => Saved.FnLock == true;
        set
        {
            try
            {
                _asus.SetFnLock(value);
                Saved.FnLock = value;
                _services.SaveSettings();
                _services.ShowOsd("\uE765", value ? "Fn-lock on: F1–F12 are function keys" : "Fn-lock off: F1–F12 are media keys");
            }
            catch (Exception ex)
            {
                _services.Report($"Fn-lock failed: {ex.Message}");
            }
            OnPropertyChanged();
        }
    }

    public void ToggleFnLock()
    {
        if (!SupportsFnLock) { _services.Report("This laptop has no firmware Fn-lock."); return; }
        FnLock = !FnLock;
    }

    public ICommand ToggleTouchpadCommand => new RelayCommand(() =>
        Runner?.Invoke(new AiO.Core.Keyboard.KeyMapping { Action = AiO.Core.Keyboard.KeyActionType.ToggleTouchpad, Target = null }));

    public ICommand ToggleCameraCommand => new RelayCommand(ToggleCamera);

    /// <summary>Runs a key action (set by the main view model), for buttons that mirror hotkey actions.</summary>
    public Action<AiO.Core.Keyboard.KeyMapping>? Runner { get; init; }

    public void ToggleCamera()
    {
        try
        {
            _services.ShowOsd("\uE714", _asus.ToggleCamera() switch
            {
                true => "Camera on",
                false => "Camera off",
                null => "This laptop doesn't let software switch the camera",
            });
        }
        catch (Exception ex)
        {
            _services.Report($"Camera toggle failed: {ex.Message}");
        }
    }

    /// <summary>Hotkey action: switches to the next lighting effect and applies it.</summary>
    public void NextAuraMode()
    {
        var modes = AuraModes;
        int index = Array.IndexOf(modes.ToArray(), AuraMode);
        AuraMode = modes[(index + 1) % modes.Count];
        ApplyKeyboard();
    }

    /// <summary>Hotkey action: one brightness step up (wrapping from max to off) or down.</summary>
    public int StepKeyboardBrightness(int delta)
    {
        int level = Saved.KeyboardBrightness + delta;
        level = level > 3 ? 0 : Math.Max(0, level);
        KeyboardBrightness = level;
        try
        {
            AsusAura.SetBrightness(level, _asus.Acpi);
            _services.SaveSettings();
        }
        catch (Exception ex)
        {
            _services.Report($"Keyboard backlight failed: {ex.Message}");
        }
        return level;
    }

    private void ApplyKeyboard()
    {
        try
        {
            var result = StartupApplier.ApplyAura(Saved, _asus);
            Saved.ManageKeyboard = true;
            _services.SaveSettings();
            _services.Report(result.Any
                ? $"Keyboard lighting sent via {result}."
                : "No ASUS keyboard lighting interface accepted the command. Is G-Helper or Armoury Crate running? Close them and try again.");
        }
        catch (Exception ex)
        {
            _services.Report($"Keyboard lighting failed: {ex.Message}");
        }
    }
}
