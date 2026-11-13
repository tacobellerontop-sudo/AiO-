using System.Text;
using AiO.Core.Hid;

namespace AiO.Core.Asus;

/// <summary>Built-in animations of the Slash LED bar on the lid of the 2024+ Zephyrus G14 / G16.</summary>
public enum SlashEffect
{
    Static,
    Bounce,
    Slash,
    Loading,
    BitStream,
    Transmission,
    Flow,
    Flux,
    Phantom,
    Spectrum,
    Hazard,
    Interfacing,
    Ramp,
    GameOver,
    Start,
    Buzzer,
}

/// <summary>
/// The Slash lid lighting. It is a vendor HID interface on the ASUS keyboard device, driven with
/// 128-byte feature reports. Command bytes and effect ids are firmware facts catalogued by G-Helper.
/// </summary>
public static class AsusSlash
{
    private const int PacketLength = 128;

    /// <summary>Slash-equipped models, by BIOS model name.</summary>
    public static bool IsSlashModel(string model) =>
        model.Contains("GA403", StringComparison.OrdinalIgnoreCase)
        || model.Contains("GA605", StringComparison.OrdinalIgnoreCase)
        || model.Contains("GU605", StringComparison.OrdinalIgnoreCase);

    public static byte EffectCode(SlashEffect effect) => effect switch
    {
        SlashEffect.Static => 0x06,
        SlashEffect.Bounce => 0x10,
        SlashEffect.Slash => 0x12,
        SlashEffect.Loading => 0x13,
        SlashEffect.BitStream => 0x1D,
        SlashEffect.Transmission => 0x1A,
        SlashEffect.Flow => 0x19,
        SlashEffect.Flux => 0x25,
        SlashEffect.Phantom => 0x24,
        SlashEffect.Spectrum => 0x26,
        SlashEffect.Hazard => 0x32,
        SlashEffect.Interfacing => 0x33,
        SlashEffect.Ramp => 0x34,
        SlashEffect.GameOver => 0x42,
        SlashEffect.Start => 0x43,
        SlashEffect.Buzzer => 0x44,
        _ => 0x06,
    };

    /// <summary>Brightness 0–3 as the firmware's 0–255 byte.</summary>
    public static byte BrightnessByte(int level) => (byte)Math.Round(Math.Clamp(level, 0, 3) * 85.333);

    private static byte Flag(bool on) => on ? (byte)0x00 : (byte)0x80;

    // ---- commands (without the report id) ----------------------------------------------

    public static byte[] Handshake => Encoding.ASCII.GetBytes("ASUS Tech.Inc.");
    public static readonly byte[] WakeUp1 = [0xC2];
    public static readonly byte[] WakeUp2 = [0xD1, 0x01, 0x00, 0x01];
    public static readonly byte[] Init1 = [0xD7, 0x00, 0x00, 0x01, 0xAC];
    public static readonly byte[] Init2 = [0xD2, 0x02, 0x01, 0x08, 0xAB];
    public static readonly byte[] SaveCommand = [0xD4, 0x00, 0x00, 0x01, 0xAB];

    public static byte[] Enable(bool on) => [0xD8, 0x02, 0x00, 0x01, Flag(on)];

    public static byte[] Effect(SlashEffect effect) =>
        [0xD3, 0x04, 0x00, 0x0C, 0x01, EffectCode(effect), 0x02, 0x42, 0x03, 0x13, 0x04, 0x11, 0x05, 0x12, 0x06, 0x13];

    /// <summary>Brightness 0–3 and animation interval 0–5 (higher = slower).</summary>
    public static byte[] Options(bool on, int brightness, int interval) =>
        [0xD3, 0x03, 0x01, 0x08, 0xAB, 0xFF, 0x01, on ? (byte)0x01 : (byte)0x00, 0x06, BrightnessByte(brightness), 0xFF, (byte)Math.Clamp(interval, 0, 5)];

    public static byte[] OnBattery(bool on) => [0xD8, 0x01, 0x00, 0x01, Flag(on)];

    public static byte[] OnLidClosed(bool on) => [0xD8, 0x00, 0x00, 0x02, 0xA5, Flag(on)];

    /// <summary>Full sequence to show an effect (or turn the bar off).</summary>
    public static IReadOnlyList<byte[]> Sequence(bool on, SlashEffect effect, int brightness, int interval, bool onBattery, bool onLidClosed)
    {
        var list = new List<byte[]> { Handshake, WakeUp1, WakeUp2 };
        if (!on || brightness <= 0)
        {
            list.Add(Enable(false));
            return list;
        }
        list.Add(Enable(true));
        list.Add(Init1);
        list.Add(Init2);
        list.Add(Effect(effect));
        list.Add(Options(true, brightness, interval));
        list.Add(SaveCommand);
        list.Add(OnBattery(onBattery));
        list.Add(OnLidClosed(onLidClosed));
        return list;
    }

    /// <summary>Report id + command, padded to the 128-byte packet.</summary>
    public static byte[] Packet(byte reportId, byte[] command, int length = PacketLength)
    {
        var packet = new byte[Math.Max(length, command.Length + 1)];
        packet[0] = reportId;
        command.CopyTo(packet, 1);
        return packet;
    }

    /// <summary>Candidate interfaces: (device, report id). The dedicated Slash device first.</summary>
    internal static IEnumerable<(HidDeviceInfo Device, byte ReportId)> Candidates(IEnumerable<HidDeviceInfo> devices)
    {
        var list = devices.Where(d => d.VendorId == AsusAura.VendorId && d.FeatureReportLength >= PacketLength).ToList();
        foreach (var d in list.Where(d => d.ProductId == 0x193B)) yield return (d, 0x5E);
        foreach (var d in list.Where(d => d.ProductId == 0x19B6)) yield return (d, 0x5D);
    }

    /// <summary>Sends the sequence to the first interface that accepts it. Returns false when none did.</summary>
    public static bool Apply(bool on, SlashEffect effect, int brightness, int interval, bool onBattery, bool onLidClosed)
    {
        var commands = Sequence(on, effect, brightness, interval, onBattery, onLidClosed);
        foreach (var (info, reportId) in Candidates(HidEnumerator.Enumerate(AsusAura.VendorId)))
        {
            try
            {
                using var connection = HidConnection.Open(info);
                foreach (var command in commands)
                    connection.SetFeature(Packet(reportId, command, Math.Max(info.FeatureReportLength, PacketLength)));
                return true;
            }
            catch
            {
                // Wrong collection or report id; try the next one.
            }
        }
        return false;
    }
}
