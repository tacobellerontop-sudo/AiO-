using System.Text;
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

/// <summary>Which channels reached the keyboard, for troubleshooting.</summary>
public sealed record AuraResult(int HidDevicesWritten, bool AcpiUsed)
{
    public bool Any => HidDevicesWritten > 0 || AcpiUsed;

    public override string ToString() =>
        $"{HidDevicesWritten} USB lighting interface(s){(AcpiUsed ? " + ACPI keyboard" : "")}";
}

/// <summary>
/// Keyboard backlight control for ASUS laptops, following G-Helper's approach:
/// <list type="bullet">
/// <item>ROG/Strix/Zephyrus: Aura messages to the ASUS N-KEY HID device (vendor 0x0B05) as output reports,
///   brightness as a feature report on the "input" report id.</item>
/// <item>TUF/Vivobook/Zenbook: RGB, brightness and power state through the ATKACPI driver.</item>
/// </list>
/// Both channels are tried, since the firmware ignores the one it doesn't use.
/// </summary>
public static class AsusAura
{
    public const ushort VendorId = 0x0B05;
    public const ushort VendorUsagePage = 0xFF31;
    public const byte AuraReportId = 0x5D;
    public const byte InputReportId = 0x5A;

    // Product ids of ASUS laptop keyboard (N-KEY) devices with Aura lighting, from G-Helper.
    private static readonly HashSet<ushort> AuraProductIds =
    [
        0x1A30, 0x1854, 0x1869, 0x1866, 0x19B6, 0x1822, 0x1837, 0x184A, 0x183D, 0x8502, 0x1807, 0x17E0,
        0x1ABE, 0x1B4C, 0x1B6E, 0x1B2C, 0x8854, 0x1CE7, 0x1CE6, 0x1BF2, 0x1BF3, 0x1CD7, 0x1CD8, 0x4B42, 0x18C6,
    ];

    // ACPI device ids for TUF-style keyboards.
    private const uint AcpiKeyboardRgb = 0x00100056;
    private const uint AcpiKeyboardRgb2 = 0x0010005A;
    private const uint AcpiKeyboardState = 0x00100057;
    private const uint AcpiKeyboardBrightness = 0x00050021;

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

    /// <summary>
    /// Which lighting zones are on in each power state. AiO turns everything on for boot and awake,
    /// and leaves sleep/shutdown on too (the firmware default), matching G-Helper's defaults.
    /// </summary>
    public static readonly byte[] PowerAllOnMessage = [AuraReportId, 0xBD, 0x01, 0xFF, 0x1F, 0xFF, 0xFF, 0xFF];

    /// <summary>Handshake both report ids expect before the first command (as the Linux hid-asus driver does).</summary>
    public static byte[] BuildInitMessage(byte reportId) => [reportId, .. Encoding.ASCII.GetBytes("ASUS Tech.Inc."), 0];

    /// <summary>Brightness 0 (off) to 3 (max).</summary>
    public static byte[] BuildBrightnessMessage(int level) => [InputReportId, 0xBA, 0xC5, 0xC4, (byte)Math.Clamp(level, 0, 3)];

    /// <summary>TUF ACPI RGB payload: command, mode, R, G, B, speed.</summary>
    public static byte[] BuildAcpiRgbPayload(byte command, AuraMode mode, RgbColor color, AuraSpeed speed) =>
        [command, (byte)mode, color.R, color.G, color.B, SpeedByte(speed)];

    /// <summary>TUF ACPI power state: backlight on at boot and while awake.</summary>
    public const int AcpiPowerBootAndAwake = 0xBD | (0x03 << 16) | (0x0C << 16) | (0x01 << 8);

    private static IReadOnlyList<HidDeviceInfo> Devices() =>
        HidEnumerator.Enumerate(VendorId)
            .Where(d => (AuraProductIds.Contains(d.ProductId) || d.UsagePage == VendorUsagePage) && d.FeatureReportLength > 0)
            .ToList();

    public static bool IsAvailable() => Devices().Count > 0;

    /// <summary>Sets the backlight effect. <paramref name="acpi"/> enables the TUF/Vivobook channel.</summary>
    public static AuraResult SetMode(AuraMode mode, RgbColor color, RgbColor color2, AuraSpeed speed, AsusAcpi? acpi = null)
    {
        int written = WriteAll(
            features: [BuildInitMessage(AuraReportId)],
            outputs: [PowerAllOnMessage, BuildModeMessage(mode, color, color2, speed), SetMessage, ApplyMessage]);

        bool acpiUsed = false;
        if (acpi is not null && acpi.IsSupported(AcpiKeyboardRgb))
        {
            acpi.Set(AcpiKeyboardState, AcpiPowerBootAndAwake);
            if (acpi.Set(AcpiKeyboardRgb, BuildAcpiRgbPayload(0xB4, mode, color, speed)) != 1)
            {
                acpi.Set(AcpiKeyboardRgb2, BuildAcpiRgbPayload(0xB3, mode, color, speed));
                acpi.Set(AcpiKeyboardRgb2, BuildAcpiRgbPayload(0xB4, mode, color, speed));
            }
            acpiUsed = true;
        }
        return new AuraResult(written, acpiUsed);
    }

    /// <summary>Brightness 0-3 over HID and, when available, ACPI.</summary>
    public static AuraResult SetBrightness(int level, AsusAcpi? acpi = null)
    {
        int written = WriteAll(features: [BuildInitMessage(InputReportId), BuildBrightnessMessage(level)], outputs: []);
        bool acpiUsed = false;
        if (acpi is not null && acpi.IsSupported(AcpiKeyboardBrightness))
        {
            acpi.Set(AcpiKeyboardBrightness, 0x80 | (Math.Clamp(level, 0, 3) & 0x7F));
            acpiUsed = true;
        }
        return new AuraResult(written, acpiUsed);
    }

    /// <summary>
    /// Sends feature reports and output reports to every candidate interface, padding each to the
    /// interface's report length. Interfaces that don't carry a report id reject it, which is expected.
    /// Returns how many interfaces accepted at least one message.
    /// </summary>
    private static int WriteAll(IReadOnlyList<byte[]> features, IReadOnlyList<byte[]> outputs)
    {
        int accepted = 0;
        foreach (var info in Devices())
        {
            HidConnection connection;
            try { connection = HidConnection.Open(info); }
            catch { continue; }

            using (connection)
            {
                bool any = false;
                foreach (var message in features)
                    any |= Try(() => connection.SetFeature(Pad(message, info.FeatureReportLength)));
                foreach (var message in outputs)
                {
                    // G-Helper writes Aura commands as output reports; fall back to a feature report.
                    bool sent = info.OutputReportLength > 0 && Try(() => connection.WriteOutput(Pad(message, info.OutputReportLength)));
                    if (!sent) sent = Try(() => connection.SetFeature(Pad(message, info.FeatureReportLength)));
                    any |= sent;
                }
                if (any) accepted++;
            }
        }
        return accepted;
    }

    private static byte[] Pad(byte[] message, int length)
    {
        var report = new byte[Math.Max(length, message.Length)];
        message.CopyTo(report, 0);
        return report;
    }

    private static bool Try(Action action)
    {
        try { action(); return true; }
        catch { return false; }
    }
}
