using AiO.Core.Native;

namespace AiO.Core.Asus;

public sealed record AsusSensors(int? CpuFanRpm, int? GpuFanRpm, int? MidFanRpm, int? CpuTemperature, int? GpuTemperature);

public sealed record GpuModeChange(bool Applied, bool RebootRequired, string Message);

/// <summary>Power limits in watts (null = leave unchanged).</summary>
public sealed record AsusPowerLimits(int? Sustained, int? Slow, int? Fast, int? GpuDynamicBoost, int? GpuTempTarget);

/// <summary>
/// G-Helper style control of ASUS ROG / TUF / Vivobook laptops: performance modes,
/// GPU modes, fan curves, battery charge limit, panel overdrive and power limits.
/// </summary>
public sealed class AsusLaptop : IDisposable
{
    private readonly AsusAcpi _acpi;

    public AsusLaptop(AsusAcpi acpi) => _acpi = acpi;

    public static AsusLaptop? TryOpen() => AsusAcpi.TryOpen() is { } acpi ? new AsusLaptop(acpi) : null;

    /// <summary>Opens the laptop controls, or explains why they're unavailable.</summary>
    public static (AsusLaptop? Laptop, AsusDetection Detection) Detect()
    {
        var acpi = AsusAcpi.TryOpen(out int error);
        var (manufacturer, model) = SystemInfo.ReadBios();
        return (acpi is null ? null : new AsusLaptop(acpi), AsusDetection.Describe(manufacturer, model, acpi is not null, error));
    }

    private uint GpuEcoId => _acpi.IsSupported(AsusDeviceIds.GpuEcoRog) ? AsusDeviceIds.GpuEcoRog : AsusDeviceIds.GpuEcoVivo;
    private uint GpuMuxId => _acpi.IsSupported(AsusDeviceIds.GpuMuxRog) ? AsusDeviceIds.GpuMuxRog : AsusDeviceIds.GpuMuxVivo;

    public bool SupportsGpuModes => _acpi.IsSupported(AsusDeviceIds.GpuEcoRog) || _acpi.IsSupported(AsusDeviceIds.GpuEcoVivo);
    public bool SupportsMux => _acpi.IsSupported(AsusDeviceIds.GpuMuxRog) || _acpi.IsSupported(AsusDeviceIds.GpuMuxVivo);
    public bool SupportsBatteryLimit => _acpi.IsSupported(AsusDeviceIds.BatteryLimit);
    public bool SupportsOverdrive => _acpi.Get(AsusDeviceIds.ScreenOverdriveSupport) == 1 || _acpi.IsSupported(AsusDeviceIds.ScreenOverdrive);
    public bool SupportsFanCurves => _acpi.IsSupported(AsusDeviceIds.CpuFanCurve);
    public bool SupportsMidFan => _acpi.IsSupported(AsusDeviceIds.MidFanSpeed);
    public bool SupportsPowerLimits => _acpi.IsSupported(AsusDeviceIds.PptSustained) || _acpi.IsSupported(AsusDeviceIds.PptSlow);
    public bool SupportsGpuPowerTuning => _acpi.IsSupported(AsusDeviceIds.GpuDynamicBoost);

    // ---- Performance modes -------------------------------------------------

    /// <summary>
    /// Sets the firmware thermal policy and matches the Windows power-mode overlay
    /// (Silent → best efficiency, Balanced → balanced, Turbo → best performance).
    /// </summary>
    public bool SetPerformanceMode(AsusPerformanceMode mode, bool syncWindowsPowerMode = true)
    {
        int status;
        if (_acpi.IsSupported(AsusDeviceIds.PerformanceMode))
            status = _acpi.Set(AsusDeviceIds.PerformanceMode, (int)mode);
        else
            status = _acpi.Set(AsusDeviceIds.VivoBookMode, VivoMode(mode));

        if (syncWindowsPowerMode)
        {
            try
            {
                PowrProf.PowerSetActiveOverlayScheme(mode switch
                {
                    AsusPerformanceMode.Silent => PowrProf.BestPowerEfficiency,
                    AsusPerformanceMode.Turbo => PowrProf.BestPerformance,
                    _ => PowrProf.Balanced,
                });
            }
            catch (EntryPointNotFoundException)
            {
                // Older Windows builds have no overlay schemes.
            }
        }
        return status == 1;
    }

    // Vivobook firmware swaps Turbo and Silent relative to ROG/TUF.
    private static int VivoMode(AsusPerformanceMode mode) => mode switch
    {
        AsusPerformanceMode.Turbo => 2,
        AsusPerformanceMode.Silent => 1,
        _ => 0,
    };

    // ---- GPU modes ---------------------------------------------------------

    public AsusGpuMode? GetGpuMode()
    {
        if (!SupportsGpuModes) return null;
        if (SupportsMux && _acpi.Get(GpuMuxId) == 0) return AsusGpuMode.Ultimate;
        return _acpi.Get(GpuEcoId) == 1 ? AsusGpuMode.Eco : AsusGpuMode.Standard;
    }

    /// <summary>
    /// Eco turns the dGPU off, Standard is Optimus/hybrid, Ultimate routes the panel straight to the
    /// dGPU through the MUX switch. Changing the MUX only takes effect after a reboot.
    /// </summary>
    public GpuModeChange SetGpuMode(AsusGpuMode target)
    {
        var current = GetGpuMode();
        if (current is null) return new(false, false, "This laptop does not expose GPU modes.");
        if (current == target) return new(true, false, $"Already in {target} mode.");

        switch (target)
        {
            case AsusGpuMode.Ultimate:
                if (!SupportsMux) return new(false, false, "This laptop has no MUX switch.");
                if (current == AsusGpuMode.Eco) _acpi.Set(GpuEcoId, 0);
                _acpi.Set(GpuMuxId, 0);
                return new(true, true, "Ultimate mode is set. Restart Windows to apply it.");

            case AsusGpuMode.Standard when current == AsusGpuMode.Ultimate:
                _acpi.Set(GpuMuxId, 1);
                return new(true, true, "Standard mode is set. Restart Windows to apply it.");

            case AsusGpuMode.Eco when current == AsusGpuMode.Ultimate:
                return new(false, false, "Switch to Standard and restart before choosing Eco.");

            case AsusGpuMode.Standard:
                return new(_acpi.Set(GpuEcoId, 0) == 1, false, "Standard mode: the dGPU is enabled.");

            case AsusGpuMode.Eco:
                return new(_acpi.Set(GpuEcoId, 1) == 1, false,
                    "Eco mode: the dGPU is off. Close apps using the dGPU first if this doesn't take effect.");
        }
        return new(false, false, "Unknown GPU mode.");
    }

    // ---- Battery / screen --------------------------------------------------

    public bool SetBatteryLimit(int percent) => _acpi.Set(AsusDeviceIds.BatteryLimit, Math.Clamp(percent, 40, 100)) == 1;

    public bool? GetOverdrive() => SupportsOverdrive ? _acpi.Get(AsusDeviceIds.ScreenOverdrive) == 1 : null;

    public bool SetOverdrive(bool enabled) => _acpi.Set(AsusDeviceIds.ScreenOverdrive, enabled ? 1 : 0) == 1;

    // ---- Fans and sensors --------------------------------------------------

    public AsusSensors ReadSensors() => new(
        FanRpm(AsusDeviceIds.CpuFanSpeed),
        FanRpm(AsusDeviceIds.GpuFanSpeed),
        SupportsMidFan ? FanRpm(AsusDeviceIds.MidFanSpeed) : null,
        Temperature(AsusDeviceIds.CpuTemperature),
        Temperature(AsusDeviceIds.GpuTemperature));

    private int? FanRpm(uint id)
    {
        int raw = _acpi.Get(id);
        int value = raw & 0xFFFF;
        if (raw < 0 && value == 0 || value > 120) return null;
        return value * 100; // firmware reports hundreds of RPM
    }

    private int? Temperature(uint id)
    {
        int value = _acpi.Get(id);
        return value is > 0 and < 150 ? value : null;
    }

    private static uint CurveId(AsusFan fan) => fan switch
    {
        AsusFan.Gpu => AsusDeviceIds.GpuFanCurve,
        AsusFan.Mid => AsusDeviceIds.MidFanCurve,
        _ => AsusDeviceIds.CpuFanCurve,
    };

    /// <summary>Reads the firmware's built-in curve for a fan in a given mode.</summary>
    public FanCurve? GetFanCurve(AsusFan fan, AsusPerformanceMode mode)
    {
        var curve = FanCurve.FromBytes(_acpi.GetBuffer(CurveId(fan), AsusAcpiProtocol.FanCurveModeArgument(mode)));
        return curve.IsValid() ? curve : null;
    }

    /// <summary>
    /// Applies a custom curve. It replaces the curve of the current performance mode until the
    /// mode changes, so callers re-apply it after <see cref="SetPerformanceMode"/>.
    /// </summary>
    public bool SetFanCurve(AsusFan fan, FanCurve curve)
    {
        if (!curve.IsValid()) throw new ArgumentException("Fan curve is not valid", nameof(curve));
        return _acpi.Set(CurveId(fan), curve.ToBytes()) == 1;
    }

    // ---- Power limits ------------------------------------------------------

    public void SetPowerLimits(AsusPowerLimits limits)
    {
        if (limits.Sustained is int spl && _acpi.IsSupported(AsusDeviceIds.PptSustained))
            _acpi.Set(AsusDeviceIds.PptSustained, Math.Clamp(spl, 5, 150));
        if (limits.Slow is int sppt && _acpi.IsSupported(AsusDeviceIds.PptSlow))
            _acpi.Set(AsusDeviceIds.PptSlow, Math.Clamp(sppt, 5, 150));
        if (limits.Fast is int fppt && _acpi.IsSupported(AsusDeviceIds.PptFast))
            _acpi.Set(AsusDeviceIds.PptFast, Math.Clamp(fppt, 5, 150));
        if (limits.GpuDynamicBoost is int boost && _acpi.IsSupported(AsusDeviceIds.GpuDynamicBoost))
            _acpi.Set(AsusDeviceIds.GpuDynamicBoost, Math.Clamp(boost, 5, 25));
        if (limits.GpuTempTarget is int temp && _acpi.IsSupported(AsusDeviceIds.GpuTempTarget))
            _acpi.Set(AsusDeviceIds.GpuTempTarget, Math.Clamp(temp, 75, 87));
    }

    // ---- Keyboard (TUF models without an Aura HID device) -------------------

    public void SetAcpiKeyboardBrightness(int level) =>
        _acpi.Set(AsusDeviceIds.KeyboardBrightnessAcpi, 0x80 | (Math.Clamp(level, 0, 3) & 0x7F));

    public void Dispose() => _acpi.Dispose();
}
