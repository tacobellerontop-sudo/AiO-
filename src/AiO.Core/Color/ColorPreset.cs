using System.Text.Json.Serialization;
using AiO.Core.Gpu;

namespace AiO.Core.Color;

/// <summary>
/// A named colour setup for a display. GPU and monitor values are stored relative to each device's own
/// range, so one preset works on NVIDIA and AMD, and on any monitor. Null fields are left untouched.
/// </summary>
public sealed record ColorPreset
{
    public string Name { get; init; } = "Preset";

    /// <summary>Software gamma-ramp colour (temperature, gamma, brightness, contrast, gains).</summary>
    public ColorAdjustment Gamma { get; init; } = ColorAdjustment.Neutral;

    /// <summary>GPU vibrance/saturation: -1 = minimum, 0 = driver default, +1 = maximum.</summary>
    public double? Saturation { get; init; }

    /// <summary>Monitor brightness through DDC/CI, 0-1 of the monitor's range.</summary>
    public double? MonitorBrightness { get; init; }

    /// <summary>Monitor contrast through DDC/CI, 0-1 of the monitor's range.</summary>
    public double? MonitorContrast { get; init; }

    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    public override string ToString() => Name;

    public static IReadOnlyList<ColorPreset> BuiltIn { get; } =
    [
        new() { Name = "Default", Gamma = ColorAdjustment.Neutral, Saturation = 0, IsBuiltIn = true },
        new() { Name = "Vivid", Gamma = new ColorAdjustment { Contrast = 1.05 }, Saturation = 0.5, IsBuiltIn = true },
        new() { Name = "Competitive", Gamma = new ColorAdjustment { Gamma = 1.25, Brightness = 0.03 }, Saturation = 0.7, IsBuiltIn = true },
        new() { Name = "Movie", Gamma = new ColorAdjustment { Gamma = 0.95, Temperature = 6200 }, Saturation = 0.15, IsBuiltIn = true },
        new() { Name = "Reading", Gamma = new ColorAdjustment { Temperature = 5000, Contrast = 0.95 }, Saturation = -0.3, IsBuiltIn = true },
        new() { Name = "Night", Gamma = new ColorAdjustment { Temperature = 3400, Brightness = -0.05 }, Saturation = 0, IsBuiltIn = true },
    ];
}

public static class ColorPresetMath
{
    /// <summary>Relative -1..1 (0 = default) to a driver value inside the range.</summary>
    public static int FromRelative(GpuColorRange range, double relative)
    {
        relative = Math.Clamp(relative, -1, 1);
        double value = relative >= 0
            ? range.Default + relative * (range.Max - range.Default)
            : range.Default + relative * (range.Default - range.Min);
        return (int)Math.Round(Math.Clamp(value, range.Min, range.Max));
    }

    /// <summary>Driver value to relative -1..1 (0 = default).</summary>
    public static double ToRelative(GpuColorRange range, int value)
    {
        if (value >= range.Default)
            return range.Max == range.Default ? 0 : (double)(value - range.Default) / (range.Max - range.Default);
        return range.Default == range.Min ? 0 : -(double)(range.Default - value) / (range.Default - range.Min);
    }

    /// <summary>Approximate colour of a white point, for drawing a preset's swatch.</summary>
    public static (byte R, byte G, byte B) Swatch(ColorPreset preset)
    {
        var (r, g, b) = GammaRamp.KelvinToRgb(preset.Gamma.Temperature);
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
