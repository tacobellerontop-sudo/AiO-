using AiO.Core.Asus;
using AiO.Core.Color;
using AiO.Core.Razer;
using AiO.Core.Settings;

namespace AiO.App.Infrastructure;

/// <summary>
/// Re-applies saved settings at launch. Several of them (gamma ramps, ASUS battery limit and fan
/// curves) are reset by Windows or firmware on reboot, which is why tools like G-Helper stay resident.
/// </summary>
public static class StartupApplier
{
    public static void ApplyAll(AppServices services)
    {
        var settings = services.Settings.Current;
        Try("colour", () => ApplyColor(services, settings));
        if (services.Asus is { } asus) Try("ASUS", () => ApplyAsus(asus, settings.Asus));
        if (settings.Asus.ManageKeyboard) Try("ASUS keyboard", () => ApplyAura(settings.Asus, services.Asus));
    }

    private static void Try(string area, Action action)
    {
        try { action(); }
        catch (Exception ex) { AppServices.Instance.Report($"Could not restore {area} settings: {ex.Message}"); }
    }

    private static void ApplyColor(AppServices services, AppSettings settings)
    {
        foreach (var (device, display) in settings.Displays)
        {
            if (!display.Gamma.IsNeutral) GammaController.Apply(device, display.Gamma);
            foreach (var (property, value) in display.Gpu)
                foreach (var gpu in services.GpuColor.Where(g => g.Displays.Contains(device, StringComparer.OrdinalIgnoreCase)))
                    gpu.Set(device, property, value);
        }
    }

    public static void ApplyAsus(AsusLaptop asus, AsusSettings s)
    {
        ApplyPerformanceMode(asus, s, s.PerformanceMode);
        if (asus.SupportsBatteryLimit && s.BatteryLimit < 100) asus.SetBatteryLimit(s.BatteryLimit);
        if (s.PanelOverdrive is bool overdrive && asus.SupportsOverdrive) asus.SetOverdrive(overdrive);
    }

    /// <summary>Switches mode, then applies that mode's custom fan curves and power limits.</summary>
    public static void ApplyPerformanceMode(AsusLaptop asus, AsusSettings s, AsusPerformanceMode mode)
    {
        asus.SetPerformanceMode(mode, s.SyncWindowsPowerMode);
        var profile = s.Profile(mode);

        if (profile.CustomFanCurves && asus.SupportsFanCurves)
        {
            if (profile.CpuCurve?.ToCurve() is { } cpu && cpu.IsValid()) asus.SetFanCurve(AsusFan.Cpu, cpu);
            if (profile.GpuCurve?.ToCurve() is { } gpu && gpu.IsValid()) asus.SetFanCurve(AsusFan.Gpu, gpu);
            if (asus.SupportsMidFan && profile.MidCurve?.ToCurve() is { } mid && mid.IsValid()) asus.SetFanCurve(AsusFan.Mid, mid);
        }

        if (profile.CustomPowerLimits && asus.SupportsPowerLimits)
        {
            asus.SetPowerLimits(new AsusPowerLimits(profile.SustainedWatts, profile.SlowWatts, profile.FastWatts,
                profile.GpuDynamicBoost, profile.GpuTempTarget));
        }
    }

    /// <summary>Applies brightness and effect over every channel the laptop has; returns what was reached.</summary>
    public static AuraResult ApplyAura(AsusSettings s, AsusLaptop? asus)
    {
        var acpi = asus?.Acpi;
        var brightness = AsusAura.SetBrightness(s.KeyboardBrightness, acpi);
        var mode = AsusAura.SetMode(s.AuraMode, RgbColor.FromHex(s.AuraColor), RgbColor.FromHex(s.AuraColor2), s.AuraSpeed, acpi);
        return new AuraResult(Math.Max(brightness.HidDevicesWritten, mode.HidDevicesWritten), brightness.AcpiUsed || mode.AcpiUsed);
    }
}
