using AiO.Core.Color;
using AiO.Core.Display;
using AiO.Core.Gpu;
using AiO.Core.Settings;

namespace AiO.App.Infrastructure;

/// <summary>Applies colour presets to displays, from the Colour page, the tray menu or a keyboard shortcut.</summary>
public static class ColorPresets
{
    public static IReadOnlyList<ColorPreset> All =>
        [.. ColorPreset.BuiltIn, .. AppServices.Instance.Settings.Current.ColorPresets];

    public static ColorPreset? Find(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies <paramref name="preset"/> to one display. <paramref name="monitor"/> is that display's DDC/CI
    /// handle, if any (DDC is slow; call this off the UI thread when a monitor is passed).
    /// </summary>
    public static void Apply(string deviceName, ColorPreset preset, DdcMonitor? monitor)
    {
        var services = AppServices.Instance;
        var all = services.Settings.Current.Displays;
        if (!all.TryGetValue(deviceName, out var saved)) all[deviceName] = saved = new DisplayColorSettings();

        if (!GammaController.Apply(deviceName, preset.Gamma))
            services.Report($"Windows refused part of \"{preset.Name}\" (the software colour). Unlock the full gamma range on the Colour page.");
        saved.Gamma = preset.Gamma;

        if (preset.Saturation is double saturation)
        {
            foreach (var gpu in services.GpuColor.Where(g => g.Displays.Contains(deviceName, StringComparer.OrdinalIgnoreCase)))
            {
                if (!gpu.SupportedProperties.Contains(GpuColorProperty.Saturation)) continue;
                if (gpu.Get(deviceName, GpuColorProperty.Saturation) is not { } range) continue;
                int value = ColorPresetMath.FromRelative(range, saturation);
                if (gpu.Set(deviceName, GpuColorProperty.Saturation, value))
                {
                    if (value == range.Default) saved.Gpu.Remove(GpuColorProperty.Saturation);
                    else saved.Gpu[GpuColorProperty.Saturation] = value;
                }
            }
        }

        if (monitor is not null)
        {
            SetRelative(monitor, VcpCode.Brightness, preset.MonitorBrightness);
            SetRelative(monitor, VcpCode.Contrast, preset.MonitorContrast);
        }

        saved.Preset = preset.Name;
        services.SaveSettings();
    }

    private static void SetRelative(DdcMonitor monitor, byte code, double? fraction)
    {
        if (fraction is not double f || monitor.Get(code) is not { Maximum: > 0 } value) return;
        monitor.Set(code, (uint)Math.Round(Math.Clamp(f, 0, 1) * value.Maximum));
    }

    /// <summary>Applies a preset to every connected display (tray menu and keyboard shortcuts).</summary>
    public static Task ApplyToAllAsync(ColorPreset preset) => Task.Run(() =>
    {
        bool needsDdc = preset.MonitorBrightness is not null || preset.MonitorContrast is not null;
        IReadOnlyList<DdcMonitor> monitors = needsDdc ? DdcMonitor.EnumerateAll() : [];
        try
        {
            foreach (var display in DisplayManager.GetDisplays())
            {
                var monitor = monitors.FirstOrDefault(m => m.DeviceName.Equals(display.DeviceName, StringComparison.OrdinalIgnoreCase));
                Apply(display.DeviceName, preset, monitor);
            }
            AppServices.Instance.Report($"Colour preset \"{preset.Name}\" applied.");
        }
        finally
        {
            foreach (var m in monitors) m.Dispose();
        }
    });

    /// <summary>Saves (or replaces) a user preset. Built-in names are reserved.</summary>
    public static bool SaveUserPreset(ColorPreset preset)
    {
        if (ColorPreset.BuiltIn.Any(b => string.Equals(b.Name, preset.Name, StringComparison.OrdinalIgnoreCase))) return false;
        var list = AppServices.Instance.Settings.Current.ColorPresets;
        list.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(preset with { IsBuiltIn = false });
        AppServices.Instance.SaveSettings();
        return true;
    }

    public static void DeleteUserPreset(string name)
    {
        AppServices.Instance.Settings.Current.ColorPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        AppServices.Instance.SaveSettings();
    }
}
