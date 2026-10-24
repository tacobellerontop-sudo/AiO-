namespace AiO.Core.Color;

/// <summary>
/// Software colour correction applied through the GPU's gamma ramp (like Windows Night Light
/// or f.lux). Works on every GPU and monitor, independent of DDC/CI.
/// </summary>
public sealed record ColorAdjustment
{
    /// <summary>Gamma exponent, 1.0 = unchanged. Higher values brighten midtones.</summary>
    public double Gamma { get; init; } = 1.0;
    /// <summary>Brightness offset, -0.5 to +0.5.</summary>
    public double Brightness { get; init; }
    /// <summary>Contrast multiplier around mid-grey, 0.5 to 1.5.</summary>
    public double Contrast { get; init; } = 1.0;
    /// <summary>White point in Kelvin, 6500 = neutral.</summary>
    public int Temperature { get; init; } = 6500;
    public double RedGain { get; init; } = 1.0;
    public double GreenGain { get; init; } = 1.0;
    public double BlueGain { get; init; } = 1.0;

    public static ColorAdjustment Neutral { get; } = new();

    public bool IsNeutral => this == Neutral;
}

public static class GammaRamp
{
    public const int Entries = 256;

    /// <summary>Builds the 3 × 256 ramp (red, then green, then blue) for SetDeviceGammaRamp.</summary>
    public static ushort[] Build(ColorAdjustment a)
    {
        var (tr, tg, tb) = KelvinToRgb(a.Temperature);
        double[] channelGain = [tr * a.RedGain, tg * a.GreenGain, tb * a.BlueGain];
        double gamma = Math.Clamp(a.Gamma, 0.3, 3.0);
        double contrast = Math.Clamp(a.Contrast, 0.5, 1.5);
        double brightness = Math.Clamp(a.Brightness, -0.5, 0.5);

        var ramp = new ushort[3 * Entries];
        for (int channel = 0; channel < 3; channel++)
        {
            for (int i = 0; i < Entries; i++)
            {
                double v = i / (double)(Entries - 1);
                v = Math.Pow(v, 1.0 / gamma);
                v = (v - 0.5) * contrast + 0.5 + brightness;
                v *= Math.Clamp(channelGain[channel], 0, 1.5);
                ramp[channel * Entries + i] = (ushort)Math.Round(Math.Clamp(v, 0, 1) * 65535);
            }
        }
        return ramp;
    }

    /// <summary>
    /// Approximate RGB multipliers for a black-body white point, normalised so 6500 K is (1, 1, 1).
    /// Based on Tanner Helland's curve fit.
    /// </summary>
    public static (double R, double G, double B) KelvinToRgb(int kelvin)
    {
        var raw = RawKelvin(Math.Clamp(kelvin, 1000, 12000));
        var white = RawKelvin(6500);
        return (Math.Min(1.0, raw.R / white.R), Math.Min(1.0, raw.G / white.G), Math.Min(1.0, raw.B / white.B));
    }

    private static (double R, double G, double B) RawKelvin(int kelvin)
    {
        double t = kelvin / 100.0;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (Math.Clamp(r, 0, 255) / 255, Math.Clamp(g, 0, 255) / 255, Math.Clamp(b, 0, 255) / 255);
    }
}
