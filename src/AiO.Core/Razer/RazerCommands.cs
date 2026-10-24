namespace AiO.Core.Razer;

/// <summary>Razer LED ids (which part of the device a lighting command targets).</summary>
public enum RazerLed : byte
{
    Zero = 0x00,
    ScrollWheel = 0x01,
    Battery = 0x03,
    Logo = 0x04,
    Backlight = 0x05,
    Underglow = 0x0A,
    RightSide = 0x10,
    LeftSide = 0x11,
}

/// <summary>Effects understood by the "extended matrix" lighting command (class 0x0F).</summary>
public enum RazerEffect : byte
{
    Off = 0x00,
    Static = 0x01,
    Breathing = 0x02,
    Spectrum = 0x03,
    Wave = 0x04,
    Reactive = 0x05,
}

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor FromHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) throw new FormatException("Expected a 6-digit hex colour");
        uint v = Convert.ToUInt32(hex, 16);
        return new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Builders for the Razer commands AiO uses.</summary>
public static class RazerCommands
{
    /// <summary>Variable storage: persist the setting in the device rather than just applying it.</summary>
    public const byte VarStore = 0x01;

    public static RazerReport GetFirmwareVersion() => new(0x00, 0x81, 0x02);

    public static RazerReport GetSerial() => new(0x00, 0x82, 0x16);

    public static RazerReport GetDpi()
    {
        var r = new RazerReport(0x04, 0x85, 0x07);
        r.Arguments[0] = VarStore;
        return r;
    }

    public static RazerReport SetDpi(int dpiX, int dpiY)
    {
        dpiX = Math.Clamp(dpiX, 100, 45000);
        dpiY = Math.Clamp(dpiY, 100, 45000);
        var r = new RazerReport(0x04, 0x05, 0x07);
        r.Arguments[0] = VarStore;
        r.Arguments[1] = (byte)(dpiX >> 8);
        r.Arguments[2] = (byte)dpiX;
        r.Arguments[3] = (byte)(dpiY >> 8);
        r.Arguments[4] = (byte)dpiY;
        return r;
    }

    public static (int X, int Y) ParseDpi(RazerReport response) =>
        ((response.Arguments[1] << 8) | response.Arguments[2], (response.Arguments[3] << 8) | response.Arguments[4]);

    public static RazerReport GetPollingRate() => new(0x00, 0x85, 0x01);

    public static RazerReport SetPollingRate(int hz)
    {
        var r = new RazerReport(0x00, 0x05, 0x01);
        r.Arguments[0] = hz switch
        {
            1000 => 0x01,
            500 => 0x02,
            125 => 0x08,
            _ => throw new ArgumentOutOfRangeException(nameof(hz), "Supported rates: 125, 500, 1000 Hz"),
        };
        return r;
    }

    public static int ParsePollingRate(RazerReport response) => response.Arguments[0] switch
    {
        0x01 => 1000,
        0x02 => 500,
        0x08 => 125,
        _ => 0,
    };

    /// <summary>Battery level, reported 0-255 in argument 1.</summary>
    public static RazerReport GetBatteryLevel() => new(0x07, 0x80, 0x02);

    public static int ParseBatteryPercent(RazerReport response) =>
        (int)Math.Round(response.Arguments[1] * 100.0 / 255.0);

    public static RazerReport GetChargingStatus() => new(0x07, 0x84, 0x02);

    public static RazerReport SetBrightness(RazerLed led, byte brightness)
    {
        var r = new RazerReport(0x0F, 0x04, 0x03);
        r.Arguments[0] = VarStore;
        r.Arguments[1] = (byte)led;
        r.Arguments[2] = brightness;
        return r;
    }

    public static RazerReport GetBrightness(RazerLed led)
    {
        var r = new RazerReport(0x0F, 0x84, 0x03);
        r.Arguments[0] = VarStore;
        r.Arguments[1] = (byte)led;
        return r;
    }

    /// <summary>
    /// Builds an extended-matrix effect command. <paramref name="colors"/> is used by Static (1 colour),
    /// Breathing (0 = random, 1 or 2 colours) and Reactive (1 colour).
    /// </summary>
    public static RazerReport SetEffect(RazerLed led, RazerEffect effect, IReadOnlyList<RgbColor>? colors = null,
        byte speed = 0x02, byte direction = 0x01)
    {
        colors ??= Array.Empty<RgbColor>();
        RazerReport r;
        switch (effect)
        {
            case RazerEffect.Static:
                r = Base(0x09, led, effect);
                r.Arguments[5] = 0x01;
                WriteColor(r, 6, colors.Count > 0 ? colors[0] : new RgbColor(0, 255, 0));
                break;

            case RazerEffect.Breathing when colors.Count >= 2:
                r = Base(0x0C, led, effect);
                r.Arguments[3] = 0x02;
                r.Arguments[5] = 0x02;
                WriteColor(r, 6, colors[0]);
                WriteColor(r, 9, colors[1]);
                break;

            case RazerEffect.Breathing when colors.Count == 1:
                r = Base(0x09, led, effect);
                r.Arguments[3] = 0x01;
                r.Arguments[5] = 0x01;
                WriteColor(r, 6, colors[0]);
                break;

            case RazerEffect.Reactive:
                r = Base(0x09, led, effect);
                r.Arguments[4] = Math.Clamp(speed, (byte)1, (byte)4);
                r.Arguments[5] = 0x01;
                WriteColor(r, 6, colors.Count > 0 ? colors[0] : new RgbColor(0, 255, 0));
                break;

            case RazerEffect.Wave:
                r = Base(0x06, led, effect);
                r.Arguments[3] = Math.Clamp(direction, (byte)0, (byte)1);
                r.Arguments[4] = 0x28;
                break;

            default: // Off, Spectrum, random Breathing
                r = Base(0x06, led, effect);
                break;
        }
        return r;
    }

    private static RazerReport Base(byte size, RazerLed led, RazerEffect effect)
    {
        var r = new RazerReport(0x0F, 0x02, size);
        r.Arguments[0] = VarStore;
        r.Arguments[1] = (byte)led;
        r.Arguments[2] = (byte)effect;
        return r;
    }

    private static void WriteColor(RazerReport r, int offset, RgbColor c)
    {
        r.Arguments[offset] = c.R;
        r.Arguments[offset + 1] = c.G;
        r.Arguments[offset + 2] = c.B;
    }
}
