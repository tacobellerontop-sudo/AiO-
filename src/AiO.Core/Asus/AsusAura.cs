using AiO.Core.Hid;
using AiO.Core.Razer;

namespace AiO.Core.Asus;

public enum AuraMode : byte
{
    Static = 0,
    Breathe = 1,
    ColorCycle = 2,
    Rainbow = 3,
    Strobe = 10,
}

public enum AuraSpeed
{
    Slow,
    Normal,
    Fast,
}

/// <summary>
/// Keyboard backlight control for ASUS laptops through the Aura HID interface
/// (vendor 0x0B05, vendor usage page 0xFF31). Message layout as documented by G-Helper.
/// </summary>
public static class AsusAura
{
    public const ushort VendorId = 0x0B05;
    public const ushort VendorUsagePage = 0xFF31;
    public const byte AuraReportId = 0x5D;
    public const byte InputReportId = 0x5A;

    public static byte[] BuildModeMessage(AuraMode mode, RgbColor color, RgbColor color2, AuraSpeed speed)
    {
        var msg = new byte[17];
        msg[0] = AuraReportId;
        msg[1] = 0xB3;
        msg[2] = 0x00; // zone: whole keyboard
        msg[3] = (byte)mode;
        msg[4] = color.R;
        msg[5] = color.G;
        msg[6] = color.B;
        msg[7] = SpeedByte(speed);
        msg[8] = 0x00; // direction
        msg[9] = mode == AuraMode.Breathe ? (byte)0x01 : (byte)0x00;
        msg[10] = color2.R;
        msg[11] = color2.G;
        msg[12] = color2.B;
        return msg;
    }

    public static byte SpeedByte(AuraSpeed speed) => speed switch
    {
        AuraSpeed.Slow => 0xE1,
        AuraSpeed.Fast => 0xF5,
        _ => 0xEB,
    };

    public static readonly byte[] SetMessage = [AuraReportId, 0xB5, 0, 0, 0];
    public static readonly byte[] ApplyMessage = [AuraReportId, 0xB4];

    /// <summary>Brightness 0 (off) to 3 (max).</summary>
    public static byte[] BuildBrightnessMessage(int level) => [InputReportId, 0xBA, 0xC5, 0xC4, (byte)Math.Clamp(level, 0, 3)];

    private static IEnumerable<HidDeviceInfo> Devices() =>
        HidEnumerator.Enumerate(VendorId).Where(d => d.UsagePage == VendorUsagePage && d.FeatureReportLength > 0);

    public static bool IsAvailable() => Devices().Any();

    public static bool SetMode(AuraMode mode, RgbColor color, RgbColor color2, AuraSpeed speed) =>
        WriteAll([BuildModeMessage(mode, color, color2, speed), SetMessage, ApplyMessage]);

    public static bool SetBrightness(int level) => WriteAll([BuildBrightnessMessage(level)]);

    /// <summary>Sends the messages to every matching interface; true if at least one accepted them.</summary>
    private static bool WriteAll(IReadOnlyList<byte[]> messages)
    {
        bool any = false;
        foreach (var info in Devices())
        {
            try
            {
                using var connection = HidConnection.Open(info);
                foreach (var message in messages)
                {
                    var report = new byte[info.FeatureReportLength];
                    Array.Copy(message, report, Math.Min(message.Length, report.Length));
                    connection.SetFeature(report);
                }
                any = true;
            }
            catch
            {
                // Interfaces that don't carry this report id reject it; that's expected.
            }
        }
        return any;
    }
}
